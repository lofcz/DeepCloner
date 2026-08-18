using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class DerivedTypeCollector
{
    public static List<TypeModel> Collect(
        INamedTypeSymbol abstractType,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores)
    {
        List<TypeModel> derivedTypes = [];
        HashSet<ITypeSymbol> processedTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        CollectIncludedTypes(abstractType, compilation, nullabilityEnabled, targetFramework, externalIgnores, derivedTypes, processedTypes);

        bool hasDisableAutoDiscovery = abstractType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerDisableAutoDiscoveryAttribute");

        if (!hasDisableAutoDiscovery)
        {
            CollectDerivedTypesFromCompilation(abstractType, compilation, nullabilityEnabled, targetFramework, externalIgnores, derivedTypes, processedTypes);
        }

        return derivedTypes;
    }

    private static void CollectIncludedTypes(
        INamedTypeSymbol abstractType,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        List<TypeModel> derivedTypes,
        HashSet<ITypeSymbol> processedTypes)
    {
        foreach (AttributeData? attr in abstractType.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "FastCloner.SourceGenerator.Shared.FastClonerIncludeAttribute")
                continue;

            if (attr.ConstructorArguments.Length == 0)
                continue;

            TypedConstant arg = attr.ConstructorArguments[0];
            if (arg.Kind != TypedConstantKind.Array)
                continue;

            foreach (TypedConstant typeConstant in arg.Values)
            {
                if (typeConstant.Value is not INamedTypeSymbol includedType)
                    continue;

                if (!IsDerivedFrom(includedType, abstractType))
                    continue;

                if (!processedTypes.Add(includedType))
                    continue;

                if (includedType.IsAbstract)
                    continue;

                // Open generic declarations (typeof(TypedRepo<>)) cannot be referenced in
                // generated dispatch; only closed constructions (typeof(TypedRepo<int>)) can.
                if (includedType is { IsGenericType: true, IsDefinition: true })
                    continue;

                TypeModel? model = CreateTypeModelForDerived(includedType, compilation, nullabilityEnabled, targetFramework, externalIgnores);
                if (model != null)
                {
                    derivedTypes.Add(model);
                }
            }
        }
    }

    private static void CollectDerivedTypesFromCompilation(
        INamedTypeSymbol abstractType,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        List<TypeModel> derivedTypes,
        HashSet<ITypeSymbol> processedTypes)
    {
        DerivedTypeVisitor visitor = new DerivedTypeVisitor(abstractType, compilation, nullabilityEnabled, targetFramework, externalIgnores, derivedTypes, processedTypes);
        visitor.Visit(compilation.GlobalNamespace);
    }

    internal static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol potentialBase)
    {
        INamedTypeSymbol? current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, potentialBase))
                return true;

            // A closed construction (Repo<string>) derives from the open declaration (Repo<T>):
            // generic roots dispatch to non-generic subtypes like StringRepo : Repo<string>.
            if (current is { IsGenericType: true } &&
                SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, potentialBase))
                return true;

            current = current.BaseType;
        }
        return false;
    }

    internal static TypeModel? CreateTypeModelForDerived(
        INamedTypeSymbol derivedType,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores)
    {
        return TypeAnalyzer.HasClonableAttribute(derivedType) ?
            CreateMinimalTypeModel(derivedType, compilation, nullabilityEnabled, targetFramework, hasOwnClonable: true) :
            CreateFullTypeModel(derivedType, compilation, nullabilityEnabled, targetFramework, externalIgnores);
    }

    private static TypeModel CreateMinimalTypeModel(
        INamedTypeSymbol type,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        bool hasOwnClonable)
    {
        (bool IsStruct, bool IsSealed, bool HasClonableBaseClass) flags = TypeAnalyzer.GetStructureFlags(type);
        bool hasParameterlessConstructor = TypeAnalyzer.HasParameterlessConstructor(type);
        
        bool trustNullability = type.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerTrustNullabilityAttribute");

        return new TypeModel(
            TypeAnalyzer.GetNamespace(type),
            type.Name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            flags.IsStruct,
            flags.IsSealed,
            type.IsAbstract,
            type.IsRecord,
            flags.HasClonableBaseClass,
            CanHaveCircularReferences: false,
            NeedsStateTracking: false,
            IsFastClonerAvailable: compilation.GetTypeByMetadataName("FastCloner.FastCloner") != null,
            Members: EquatableArray<MemberModel>.Empty,
            TypeParameters: new EquatableArray<string>(TypeAnalyzer.GetTypeParameters(type).ToArray()),
            TypeConstraints: new EquatableArray<string>(TypeAnalyzer.GetTypeConstraints(type).ToArray()),
            RelatedTypes: EquatableArray<TypeModel>.Empty,
            NestedTypes: EquatableArray<MemberModel>.Empty,
            DerivedTypes: EquatableArray<TypeModel>.Empty,
            nullabilityEnabled,
            trustNullability,
            PreserveIdentity: null,
            IsRefLikeType: false,
            hasParameterlessConstructor,
            CodeAnalysisAvailable: compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null,
            TargetFramework: targetFramework);
    }

    private static TypeModel? CreateFullTypeModel(
        INamedTypeSymbol derivedType,
        Compilation compilation,
        bool nullabilityEnabled,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores)
    {
        bool isFastClonerAvailable = compilation.GetTypeByMetadataName("FastCloner.FastCloner") != null;
        
        List<MemberAnalysis> memberAnalyses = MemberCollector.GetMembers(derivedType, compilation, nullabilityEnabled, externalIgnores);
        
        Dictionary<string, TypeModel> relatedTypes = new Dictionary<string, TypeModel>();
        Dictionary<ITypeSymbol, TypeModel?> implicitCache = new Dictionary<ITypeSymbol, TypeModel?>(SymbolEqualityComparer.Default);
        HashSet<ITypeSymbol> processingStack = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        
        List<MemberModel> finalMembers = [];
        Dictionary<string, MemberModel> nestedTypes = new Dictionary<string, MemberModel>();
        
        foreach (MemberAnalysis analysis in memberAnalyses)
        {
            MemberModel memberModel = analysis.Model;
            ITypeSymbol memberType = analysis.Type;
            
            if (memberModel.TypeKind is MemberTypeKind.Other or MemberTypeKind.Implicit)
            {
                if (ImplicitTypeAnalyzer.TryAnalyze(memberType, compilation, nullabilityEnabled, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? implicitModel))
                {
                    memberModel = memberModel with 
                    { 
                        TypeKind = MemberTypeKind.Implicit, 
                        RequiresFastCloner = false 
                    };
                    
                    if (implicitModel != null && !relatedTypes.ContainsKey(implicitModel.FullyQualifiedName))
                    {
                        relatedTypes[implicitModel.FullyQualifiedName] = implicitModel;
                        foreach (TypeModel? rel in implicitModel.RelatedTypes)
                        {
                            if (!relatedTypes.ContainsKey(rel.FullyQualifiedName))
                                relatedTypes[rel.FullyQualifiedName] = rel;
                        }
                        foreach (MemberModel nested in implicitModel.NestedTypes)
                        {
                            if (!nestedTypes.ContainsKey(nested.TypeFullName))
                                nestedTypes[nested.TypeFullName] = nested;
                        }
                    }
                }
                else if (memberModel.TypeKind == MemberTypeKind.Implicit)
                {
                    memberModel = memberModel with 
                    { 
                        TypeKind = MemberTypeKind.Other, 
                        RequiresFastCloner = true 
                    };
                }
            }
            
            if (memberModel.TypeKind != MemberTypeKind.Implicit)
            {
                NestedTypeCollector.Collect(memberType, compilation, nullabilityEnabled, nestedTypes);

                if (memberModel.TypeKind is MemberTypeKind.Collection or MemberTypeKind.Array)
                {
                    ITypeSymbol? elemType = memberModel.TypeKind == MemberTypeKind.Array 
                        ? ((IArrayTypeSymbol)memberType).ElementType 
                        : TypeAnalyzer.GetCollectionElementType(memberType, compilation);

                    if (elemType != null && memberModel is { ElementIsSafe: false, ElementHasClonableAttr: false })
                    {
                        if (ImplicitTypeAnalyzer.TryAnalyze(elemType, compilation, nullabilityEnabled, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? implicitModel))
                        {
                            if (implicitModel != null)
                            {
                                if (!relatedTypes.ContainsKey(implicitModel.FullyQualifiedName))
                                    relatedTypes[implicitModel.FullyQualifiedName] = implicitModel;
                                
                                foreach (TypeModel? rel in implicitModel.RelatedTypes)
                                    if (!relatedTypes.ContainsKey(rel.FullyQualifiedName))
                                        relatedTypes[rel.FullyQualifiedName] = rel;

                                foreach (MemberModel nested in implicitModel.NestedTypes)
                                    if (!nestedTypes.ContainsKey(nested.TypeFullName))
                                        nestedTypes[nested.TypeFullName] = nested;
                            }
                        }
                    }
                }
                else if (memberModel.TypeKind == MemberTypeKind.Dictionary)
                {
                    (ITypeSymbol KeyType, ITypeSymbol ValueType)? dictTypes = TypeAnalyzer.GetDictionaryTypes(memberType, compilation);
                    if (dictTypes.HasValue)
                    {
                        if (memberModel is { KeyIsSafe: false, KeyIsClonable: false })
                        {
                            if (ImplicitTypeAnalyzer.TryAnalyze(dictTypes.Value.KeyType, compilation, nullabilityEnabled, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? implicitKey))
                            {
                                if (implicitKey != null)
                                {
                                    if (!relatedTypes.ContainsKey(implicitKey.FullyQualifiedName))
                                        relatedTypes[implicitKey.FullyQualifiedName] = implicitKey;
                                    
                                    foreach (TypeModel? rel in implicitKey.RelatedTypes)
                                        if (!relatedTypes.ContainsKey(rel.FullyQualifiedName))
                                            relatedTypes[rel.FullyQualifiedName] = rel;

                                    foreach (MemberModel nested in implicitKey.NestedTypes)
                                        if (!nestedTypes.ContainsKey(nested.TypeFullName))
                                            nestedTypes[nested.TypeFullName] = nested;
                                }
                            }
                        }

                        if (memberModel is { ValueIsSafe: false, ValueIsClonable: false })
                        {
                            if (ImplicitTypeAnalyzer.TryAnalyze(dictTypes.Value.ValueType, compilation, nullabilityEnabled, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? implicitVal))
                            {
                                if (implicitVal != null)
                                {
                                    if (!relatedTypes.ContainsKey(implicitVal.FullyQualifiedName))
                                        relatedTypes[implicitVal.FullyQualifiedName] = implicitVal;
                                    
                                    foreach (TypeModel? rel in implicitVal.RelatedTypes)
                                        if (!relatedTypes.ContainsKey(rel.FullyQualifiedName))
                                            relatedTypes[rel.FullyQualifiedName] = rel;

                                    foreach (MemberModel nested in implicitVal.NestedTypes)
                                        if (!nestedTypes.ContainsKey(nested.TypeFullName))
                                            nestedTypes[nested.TypeFullName] = nested;
                                }
                            }
                        }
                    }
                }
            }
            
            finalMembers.Add(memberModel);
        }
        
        List<string> typeParameters = TypeAnalyzer.GetTypeParameters(derivedType);
        List<string> typeConstraints = TypeAnalyzer.GetTypeConstraints(derivedType);
        (bool IsStruct, bool IsSealed, bool HasClonableBaseClass) flags = TypeAnalyzer.GetStructureFlags(derivedType);
        
        if (!isFastClonerAvailable)
        {
            List<string> unclonableMembers = finalMembers
                .Where(m => m.RequiresFastCloner)
                .Select(m => m.Name)
                .ToList();
            
            if (unclonableMembers.Count > 0)
            {
                return null;
            }
        }
        
        bool? preserveIdentity = GetPreserveIdentityFromType(derivedType);
        
        List<string> circRefLog = [];
        (bool canHaveCircularRefs, bool needsStateTracking) = StateRequirementAnalyzer.Analyze(derivedType, compilation, circRefLog, preserveIdentity);
        bool hasParameterlessConstructor = TypeAnalyzer.HasParameterlessConstructor(derivedType);
        
        bool trustNullability = derivedType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerTrustNullabilityAttribute");
        
        return new TypeModel(
            TypeAnalyzer.GetNamespace(derivedType),
            derivedType.Name,
            derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            flags.IsStruct,
            flags.IsSealed,
            derivedType.IsAbstract,
            derivedType.IsRecord,
            flags.HasClonableBaseClass,
            canHaveCircularRefs,
            needsStateTracking,
            isFastClonerAvailable,
            new EquatableArray<MemberModel>(finalMembers.ToArray()),
            new EquatableArray<string>(typeParameters.ToArray()),
            new EquatableArray<string>(typeConstraints.ToArray()),
            new EquatableArray<TypeModel>(relatedTypes.Values.ToArray()),
            new EquatableArray<MemberModel>(nestedTypes.Values.ToArray()),
            EquatableArray<TypeModel>.Empty,
            nullabilityEnabled,
            trustNullability,
            preserveIdentity,
            IsRefLikeType: false,
            hasParameterlessConstructor,
            CodeAnalysisAvailable: compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null,
            TargetFramework: targetFramework,
            CircularAnalysisLog: new EquatableArray<string>(circRefLog.ToArray()));
    }
    
    private static bool? GetPreserveIdentityFromType(INamedTypeSymbol symbol)
    {
        foreach (AttributeData attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerPreserveIdentityAttribute")
            {
                foreach (KeyValuePair<string, TypedConstant> namedArg in attr.NamedArguments)
                {
                    if (namedArg is { Key: "Enabled", Value.Value: bool namedEnabled })
                        return namedEnabled;
                }
                
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is bool enabled)
                    return enabled;
                
                return true;
            }
        }
        return null;
    }
    
    private class DerivedTypeVisitor : SymbolVisitor
    {
        private readonly INamedTypeSymbol _baseType;
        private readonly Compilation _compilation;
        private readonly bool _nullabilityEnabled;
        private readonly TargetFramework _targetFramework;
        private readonly ExternalIgnoreRegistry _externalIgnores;
        private readonly List<TypeModel> _derivedTypes;
        private readonly HashSet<ITypeSymbol> _processedTypes;

        public DerivedTypeVisitor(
            INamedTypeSymbol baseType,
            Compilation compilation,
            bool nullabilityEnabled,
            TargetFramework targetFramework,
            ExternalIgnoreRegistry externalIgnores,
            List<TypeModel> derivedTypes,
            HashSet<ITypeSymbol> processedTypes)
        {
            _baseType = baseType;
            _compilation = compilation;
            _nullabilityEnabled = nullabilityEnabled;
            _targetFramework = targetFramework;
            _externalIgnores = externalIgnores;
            _derivedTypes = derivedTypes;
            _processedTypes = processedTypes;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (INamespaceOrTypeSymbol? member in symbol.GetMembers())
            {
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            // Generic declarations (TypedRepo<U>) are skipped: only their closed
            // constructions, registered via [FastClonerInclude(typeof(TypedRepo<int>))],
            // can appear in generated dispatch code.
            if (symbol is { IsAbstract: false, IsGenericType: false, TypeKind: TypeKind.Class } &&
                IsDerivedFrom(symbol, _baseType) &&
                _processedTypes.Add(symbol))
            {
                TypeModel? model = CreateTypeModelForDerived(symbol, _compilation, _nullabilityEnabled, _targetFramework, _externalIgnores);
                if (model != null)
                {
                    _derivedTypes.Add(model);
                }
            }
            
            foreach (INamedTypeSymbol? nestedType in symbol.GetTypeMembers())
            {
                nestedType.Accept(this);
            }
        }
    }
}

