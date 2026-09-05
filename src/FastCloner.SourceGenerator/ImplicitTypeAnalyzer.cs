using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class ImplicitTypeAnalyzer
{
    public static bool TryAnalyze(
        ITypeSymbol type,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        Dictionary<ITypeSymbol, TypeModel?> cache,
        HashSet<ITypeSymbol> processingStack,
        out TypeModel? implicitModel)
    {
        implicitModel = null;

        ITypeSymbol cacheKey = TypeAnalyzer.ToNullabilityCacheKey(type);

        if (processingStack.Contains(cacheKey))
            return true;

        if (cache.TryGetValue(cacheKey, out TypeModel? cached))
        {
            implicitModel = cached;
            return cached != null;
        }

        if (!TypeAnalyzer.IsImplicitCandidate(type))
            return false;

        if (type is not INamedTypeSymbol namedType)
            return false;

        processingStack.Add(cacheKey);

        List<MemberAnalysis> memberAnalyses = MemberCollector.GetMembers(namedType, compilation, nullabilityEnabled, externalIgnores);
        List<MemberModel> finalImplicitMembers = [];
        List<TypeModel> childRelatedTypes = [];
        Dictionary<string, MemberModel> implicitNestedMembers = new Dictionary<string, MemberModel>();
        
        bool success = true;
        bool hasUnsafeReferenceMember = false;

        foreach (MemberAnalysis analysis in memberAnalyses)
        {
            MemberModel m = analysis.Model;
            
            if (!m.IsValueType && !TypeAnalyzer.IsSafeType(analysis.Type, compilation))
            {
                hasUnsafeReferenceMember = true;
            }
            
            if (m.TypeKind is MemberTypeKind.Other or MemberTypeKind.Implicit)
            {
                if (TryAnalyze(analysis.Type, compilation, nullabilityEnabled, targetFramework, externalIgnores, cache, processingStack, out TypeModel? childModel))
                {
                    m = m with { TypeKind = MemberTypeKind.Implicit, RequiresFastCloner = false };
                    if (childModel != null) childRelatedTypes.Add(childModel);
                }
                else
                {
                    success = false;
                    break;
                }
            }
            else if (m.RequiresFastCloner)
            {
                bool handled = false;
                
                bool TryHandleComponent(ITypeSymbol componentType, out MemberModel? componentMember)
                {
                    componentMember = null;
                    if (TryAnalyze(componentType, compilation, nullabilityEnabled, targetFramework, externalIgnores, cache, processingStack, out TypeModel? compModel))
                    {
                        if (compModel != null) childRelatedTypes.Add(compModel);
                        
                        string typeName = TypeAnalyzer.GetTypeNameForSignature(componentType);
                        componentMember = new MemberModel(
                            Name: "Implicit_" + componentType.Name, 
                            TypeFullName: typeName,
                            IsReadOnly: false,
                            IsProperty: false,
                            IsField: false,
                            TypeKind: MemberTypeKind.Implicit,
                            ElementTypeName: null, KeyTypeName: null, ValueTypeName: null,
                            ElementIsSafe: false, ElementHasClonableAttr: false,
                            KeyIsSafe: false, KeyIsClonable: false,
                            ValueIsSafe: false, ValueIsClonable: false,
                            RequiresFastCloner: false,
                            CollectionKind: CollectionKind.None,
                            ConcreteTypeFullName: null,
                            IsValueType: componentType.IsValueType,
                            IsInitOnly: false,
                            IsRequired: false,
                            ArrayRank: 0,
                            IsNullable: false,
                            HasGetter: true,
                            HasSetter: true,
                            SetterIsAccessible: true,
                            MemberBehavior: MemberCloneBehavior.Clone
                        );
                        return true;
                    }
                    return false;
                }

                if (m.TypeKind is MemberTypeKind.Array or MemberTypeKind.Collection)
                {
                    ITypeSymbol? elemType = (m.TypeKind == MemberTypeKind.Array) 
                        ? ((IArrayTypeSymbol)analysis.Type).ElementType 
                        : TypeAnalyzer.GetCollectionElementType(analysis.Type, compilation);

                    if (elemType != null && TryHandleComponent(elemType, out MemberModel? elemMember))
                    {
                        m = m with { RequiresFastCloner = false };
                        if (elemMember != null) implicitNestedMembers[elemMember.Value.TypeFullName] = elemMember.Value;
                        handled = true;
                    }
                }
                else if (m.TypeKind == MemberTypeKind.Dictionary)
                {
                    (ITypeSymbol KeyType, ITypeSymbol ValueType)? dictTypes = TypeAnalyzer.GetDictionaryTypes(analysis.Type, compilation);
                    if (dictTypes.HasValue)
                    {
                        bool keyOk = m.KeyIsSafe || m.KeyIsClonable;
                        bool valOk = m.ValueIsSafe || m.ValueIsClonable;
                        
                        if (!keyOk)
                        {
                            if (TryHandleComponent(dictTypes.Value.KeyType, out MemberModel? keyMember))
                            {
                                if (keyMember != null) implicitNestedMembers[keyMember.Value.TypeFullName] = keyMember.Value;
                                keyOk = true;
                            }
                        }
                        
                        if (!valOk)
                        {
                            if (TryHandleComponent(dictTypes.Value.ValueType, out MemberModel? valMember))
                            {
                                if (valMember != null) implicitNestedMembers[valMember.Value.TypeFullName] = valMember.Value;
                                valOk = true;
                            }
                        }
                        
                        if (keyOk && valOk)
                        {
                            m = m with { RequiresFastCloner = false };
                            handled = true;
                        }
                    }
                }

                if (!handled)
                {
                    success = false; 
                    break;
                }
            }
            
            finalImplicitMembers.Add(m);
        }
        
        processingStack.Remove(cacheKey);
        
        if (success)
        {
            Dictionary<string, TypeModel> relatedTypesMap = new Dictionary<string, TypeModel>();
            foreach (TypeModel? child in childRelatedTypes)
            {
                relatedTypesMap[child.FullyQualifiedName] = child;
                foreach (TypeModel? rel in child.RelatedTypes)
                {
                    relatedTypesMap[rel.FullyQualifiedName] = rel;
                }
            }
            
            (bool IsStruct, bool IsSealed, bool HasClonableBaseClass) flags = TypeAnalyzer.GetStructureFlags(namedType);
            bool canHaveCircularRefs = hasUnsafeReferenceMember || flags.HasClonableBaseClass;
            bool hasParameterlessConstructor = TypeAnalyzer.HasParameterlessConstructor(namedType);
            bool trustNullability = namedType.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerTrustNullabilityAttribute");

            implicitModel = new TypeModel(
                TypeAnalyzer.GetNamespace(namedType),
                namedType.Name,
                TypeAnalyzer.GetTypeNameForSignature(namedType),
                flags.IsStruct,
                flags.IsSealed,
                namedType.IsAbstract,
                namedType.IsRecord,
                flags.HasClonableBaseClass,
                canHaveCircularRefs,
                canHaveCircularRefs,
                false,
                new EquatableArray<MemberModel>(finalImplicitMembers.ToArray()),
                new EquatableArray<string>(TypeAnalyzer.GetTypeParameters(namedType).ToArray()),
                new EquatableArray<string>(TypeAnalyzer.GetTypeConstraints(namedType).ToArray()),
                new EquatableArray<TypeModel>(relatedTypesMap.Values.ToArray()),
                new EquatableArray<MemberModel>(implicitNestedMembers.Values.ToArray()),
                EquatableArray<TypeModel>.Empty,
                nullabilityEnabled,
                trustNullability,
                PreserveIdentity: null,
                IsRefLikeType: false,
                hasParameterlessConstructor,
                CodeAnalysisAvailable: compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null,
                TargetFramework: targetFramework);
                
            cache[cacheKey] = implicitModel;
            return true;
        }
        
        return false;
    }
}
