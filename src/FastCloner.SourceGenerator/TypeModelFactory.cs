using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class TypeModelFactory
{
    public static bool TryCreate(
        INamedTypeSymbol symbol,
        bool nullabilityEnabled,
        Compilation compilation,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        out TypeModel? model,
        out Diagnostic? error)
    {
        model = null;
        error = null;
        
        bool isFastClonerAvailable = compilation.GetTypeByMetadataName("FastCloner.FastCloner") != null;
        bool hasSimulateNoRuntime = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerSimulateNoRuntimeAttribute");
        bool trustNullability = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerTrustNullabilityAttribute");
        bool hasPolymorphicAttribute = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerPolymorphicAttribute");
        // Abstract roots always dispatch. The attribute extends that to concrete roots (generic
        // included: closed constructions of subtypes are dispatched); sealed/static roots cannot
        // use it (misuse is reported by the polymorphic validation pipeline).
        bool isPolymorphicRoot = hasPolymorphicAttribute &&
                                 !symbol.IsAbstract &&
                                 !symbol.IsSealed &&
                                 !symbol.IsValueType;
        bool? preserveIdentity = GetPreserveIdentityFromType(symbol);
        bool codeAnalysisAvailable = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null;
        
        if (hasSimulateNoRuntime)
        {
            isFastClonerAvailable = false;
        }

        List<MemberAnalysis> memberAnalyses = MemberCollector.GetMembers(symbol, compilation, nullabilityEnabled, externalIgnores);
        Dictionary<string, TypeModel> relatedTypes = new Dictionary<string, TypeModel>(); // Use FQN as key to avoid dupes
        Dictionary<ITypeSymbol, TypeModel?> implicitCache = TypeAnalyzer.CreateNullabilityAwareMap<TypeModel?>();
        HashSet<ITypeSymbol> processingStack = TypeAnalyzer.CreateNullabilityAwareSet();
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
                else
                {
                    if (memberModel.TypeKind == MemberTypeKind.Implicit)
                    {
                        memberModel = memberModel with 
                        { 
                            TypeKind = MemberTypeKind.Other, 
                            RequiresFastCloner = true 
                        };
                    }
                }
            }
            
            if (memberModel.TypeKind != MemberTypeKind.Implicit)
            {
                NestedTypeCollector.Collect(memberType, compilation, nullabilityEnabled, nestedTypes);
                
                if (memberModel.TypeKind is MemberTypeKind.Collection or MemberTypeKind.Array)
                {
                    ITypeSymbol? elemType = (memberModel.TypeKind == MemberTypeKind.Array) 
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
        
        List<string> typeParameters = TypeAnalyzer.GetTypeParameters(symbol);
        List<string> typeConstraints = TypeAnalyzer.GetTypeConstraints(symbol);
        (bool IsStruct, bool IsSealed, bool HasClonableBaseClass) flags = TypeAnalyzer.GetStructureFlags(symbol);
        
        if (!isFastClonerAvailable)
        {
            List<string> unclonableMembers = finalMembers
                .Where(m => m.RequiresFastCloner)
                .Select(m => m.Name)
                .ToList();
            
            if (unclonableMembers.Count > 0)
            {
                string assemblyName = compilation.AssemblyName ?? string.Empty;
                bool isFastClonerAssembly = assemblyName.StartsWith("FastCloner", StringComparison.OrdinalIgnoreCase);
                DiagnosticSeverity severity = isFastClonerAssembly ? DiagnosticSeverity.Error : DiagnosticSeverity.Error;
                
                string membersList = string.Join("\n  - ", unclonableMembers);
                
                error = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FCG004",
                        "Missing FastCloner dependency for deep cloning",
                        "Type '{0}' cannot be deep cloned without the FastCloner runtime library.\n\n" +
                        "Members requiring deep cloning:\n  - {1}\n\n" +
                        "Solutions:\n" +
                        "  1. Install the FastCloner NuGet package, OR\n" +
                        "  2. Mark the member types with [FastClonerClonable] to enable source generation",
                        "FastCloner",
                        severity,
                        isEnabledByDefault: true),
                    symbol.Locations.FirstOrDefault() ?? Location.None,
                    symbol.Name,
                    membersList);
                
                return isFastClonerAssembly;
            }
        }

        List<string> circRefLog = [];
        StateRequirementAnalyzer.AnalysisResult stateAnalysis = StateRequirementAnalyzer.Analyze(symbol, compilation, circRefLog, preserveIdentity);
        bool canHaveCircularRefs = stateAnalysis.HasCircularRefs;
        bool needsStateTracking = stateAnalysis.NeedsStateTracking;
        bool hasParameterlessConstructor = TypeAnalyzer.HasParameterlessConstructor(symbol);
        bool isRefLikeType = TypeAnalyzer.IsRefStructType(symbol);
        EquatableArray<TypeModel> derivedTypes = EquatableArray<TypeModel>.Empty;
        
        if (symbol.IsAbstract || isPolymorphicRoot)
        {
            List<TypeModel> derivedTypesList = DerivedTypeCollector.Collect(symbol, compilation, nullabilityEnabled, targetFramework, externalIgnores);
            derivedTypes = new EquatableArray<TypeModel>(derivedTypesList.ToArray());

            if (symbol.IsAbstract && derivedTypesList.Count == 0 && !isFastClonerAvailable)
            {
                error = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FCG002",
                        "Abstract type has no discoverable derived types",
                        "Abstract type '{0}' has no derived types that can be discovered. " +
                        "Either add concrete derived types in this assembly, use [FastClonerInclude] to register external types, " +
                        "or install the FastCloner NuGet package for runtime fallback.",
                        "FastCloner",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    symbol.Locations.FirstOrDefault() ?? Location.None,
                    symbol.ToDisplayString());
            }
        }
        
        model = new TypeModel(
            TypeAnalyzer.GetNamespace(symbol),
            symbol.Name,
            TypeAnalyzer.GetTypeNameForSignature(symbol),
            flags.IsStruct,
            flags.IsSealed,
            symbol.IsAbstract,
            symbol.IsRecord,
            flags.HasClonableBaseClass,
            canHaveCircularRefs,
            needsStateTracking,
            isFastClonerAvailable,
            new EquatableArray<MemberModel>(finalMembers.ToArray()),
            new EquatableArray<string>(typeParameters.ToArray()),
            new EquatableArray<string>(typeConstraints.ToArray()),
            new EquatableArray<TypeModel>(relatedTypes.Values.ToArray()),
            new EquatableArray<MemberModel>(nestedTypes.Values.ToArray()),
            derivedTypes,
            nullabilityEnabled,
            trustNullability,
            preserveIdentity,
            isRefLikeType,
            hasParameterlessConstructor,
            codeAnalysisAvailable,
            isPolymorphicRoot,
            targetFramework,
            new EquatableArray<string>(circRefLog.ToArray()));

        return true;
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
}
