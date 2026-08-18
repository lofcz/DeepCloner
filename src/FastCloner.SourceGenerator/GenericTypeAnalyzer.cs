using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class GenericTypeAnalyzer
{
    public static GenericUsage? Analyze(
        INamedTypeSymbol targetSymbol,
        ITypeSymbol typeArg,
        Compilation compilation,
        bool nullability,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores)
    {
        if (typeArg.TypeKind == TypeKind.TypeParameter)
            return null;

        // Unbound arguments (e.g. List<U> from a foreign generic declaration's base list
        // like TypedRepo<U> : Repo<List<U>>) cannot appear in generated code; cloning for
        // such cases falls back to the runtime cloner.
        if (ContainsUnboundTypeParameter(typeArg))
            return null;

        bool isSafe = TypeAnalyzer.IsSafeType(typeArg, compilation);
        bool isClonable = TypeAnalyzer.HasClonableAttribute(typeArg);
        
        // Analyze for collection types (List, Dictionary, Array, etc.)
        Dictionary<string, MemberModel> nestedTypes = new Dictionary<string, MemberModel>();
        NestedTypeCollector.Collect(typeArg, compilation, nullability, nestedTypes);
        
        MemberModel? collectionModel = null;
        string typeArgFQN = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (nestedTypes.TryGetValue(typeArgFQN, out MemberModel model))
        {
            collectionModel = model;
        }

        // Analyze for implicit types (POCOs without attribute)
        Dictionary<string, TypeModel> implicitTypes = new Dictionary<string, TypeModel>();
        Dictionary<ITypeSymbol, TypeModel?> implicitCache = new Dictionary<ITypeSymbol, TypeModel?>(SymbolEqualityComparer.Default);
        HashSet<ITypeSymbol> processingStack = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        void CollectImplicitRecursively(ITypeSymbol t)
        {
            // Skip safe types, they don't need implicit helpers
            if (TypeAnalyzer.IsSafeType(t, compilation))
                return;

            // Check if this type is a candidate for implicit cloning
            if (ImplicitTypeAnalyzer.TryAnalyze(t, compilation, nullability, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? implicitModel))
            {
                if (implicitModel != null && !implicitTypes.ContainsKey(implicitModel.FullyQualifiedName))
                {
                    implicitTypes[implicitModel.FullyQualifiedName] = implicitModel;
                    // Dependencies are already collected in 'implicitModel.RelatedTypes', but we need to flatten them into our list
                    foreach (TypeModel? rel in implicitModel.RelatedTypes)
                    {
                        if (!implicitTypes.ContainsKey(rel.FullyQualifiedName))
                            implicitTypes[rel.FullyQualifiedName] = rel;
                    }
                }
            }

            // Recurse into generics/arrays to find other candidates
            if (t is INamedTypeSymbol { IsGenericType: true } named)
            {
                foreach (ITypeSymbol? arg in named.TypeArguments)
                    CollectImplicitRecursively(arg);
            }
            else if (t is IArrayTypeSymbol array)
            {
                CollectImplicitRecursively(array.ElementType);
            }
        }

        CollectImplicitRecursively(typeArg);

        // We capture if it's Safe, Clonable, a supported Collection/Dictionary, or has Implicit types
        if (isSafe || isClonable || collectionModel != null || implicitTypes.Count > 0)
        {
            string? extensionClassFQN = null;
            if (isClonable && typeArg is INamedTypeSymbol namedArg)
            {
                string ns = TypeAnalyzer.GetNamespace(namedArg);
                string name = namedArg.Name;
                string extName = $"{name}FastDeepCloneExtensions";
                extensionClassFQN = string.IsNullOrEmpty(ns) ? $"global::{extName}" : $"global::{ns}.{extName}";
            }

            return new GenericUsage(
                targetSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeArgFQN,
                extensionClassFQN,
                collectionModel,
                new EquatableArray<MemberModel>(nestedTypes.Values.ToArray()),
                new EquatableArray<TypeModel>(implicitTypes.Values.ToArray()),
                isSafe,
                isClonable
            );
        }

        return null;
    }

    internal static bool ContainsUnboundTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;
            case IArrayTypeSymbol array:
                return ContainsUnboundTypeParameter(array.ElementType);
            case INamedTypeSymbol named:
                foreach (ITypeSymbol? typeArgument in named.TypeArguments)
                {
                    if (ContainsUnboundTypeParameter(typeArgument))
                        return true;
                }
                return false;
            default:
                return false;
        }
    }
}
