using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastCloner.SourceGenerator;

/// <summary>
/// A closed construction of a generic subtype (TypedRepo&lt;int&gt;) destined for the
/// dispatch list of its polymorphic/abstract clonable root (Repo&lt;T&gt;).
/// RootFqn matches the root TypeModel's FullyQualifiedName (open declaration form).
/// </summary>
internal sealed record ClosedSubtypeUsage(string RootFqn, TypeModel Model);

/// <summary>
/// Discovers closed constructions of generic subtypes from usages, mirroring how generic
/// clonable types discover their type arguments: a generic subtype declaration
/// (TypedRepo&lt;U&gt; : Repo&lt;List&lt;U&gt;&gt;) admits infinitely many constructions, but every
/// construction that appears in source (TypedRepo&lt;int&gt;) is a dispatch candidate.
/// </summary>
internal static class SubtypeUsageCollector
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is GenericNameSyntax;
    }

    public static EquatableArray<ClosedSubtypeUsage> Collect(GeneratorSyntaxContext context, TargetFramework targetFramework, ExternalIgnoreRegistry externalIgnores, CancellationToken cancellationToken)
    {
        GenericNameSyntax node = (GenericNameSyntax)context.Node;
        INamedTypeSymbol? symbol = context.SemanticModel.GetSymbolInfo(node, cancellationToken).Symbol as INamedTypeSymbol;

        if (symbol is not { IsGenericType: true, IsDefinition: false })
            return EquatableArray<ClosedSubtypeUsage>.Empty;

        // Unbound constructions (TypedRepo<U> inside another generic declaration) cannot
        // be referenced in generated code.
        foreach (ITypeSymbol? typeArgument in symbol.TypeArguments)
        {
            if (GenericTypeAnalyzer.ContainsUnboundTypeParameter(typeArgument))
                return EquatableArray<ClosedSubtypeUsage>.Empty;
        }

        List<INamedTypeSymbol> roots = FindAllDispatchRoots(symbol);
        if (roots.Count == 0)
            return EquatableArray<ClosedSubtypeUsage>.Empty;

        bool nullability = context.SemanticModel.GetNullableContext(node.SpanStart).HasFlag(NullableContext.Enabled);
        TypeModel? model = DerivedTypeCollector.CreateTypeModelForDerived(
            symbol, context.SemanticModel.Compilation, nullability, targetFramework, externalIgnores);
        if (model == null)
            return EquatableArray<ClosedSubtypeUsage>.Empty;

        // Every dispatch root in the chain needs the branch (nested polymorphic roots:
        // cloning through either base reference must dispatch).
        List<ClosedSubtypeUsage> usages = [];
        foreach (INamedTypeSymbol chainRoot in FindAllDispatchRoots(symbol))
        {
            usages.Add(new ClosedSubtypeUsage(
                chainRoot.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                model));
        }

        return new EquatableArray<ClosedSubtypeUsage>(usages.ToArray());
    }

    /// <summary>
    /// Walks the base chain for clonable bases that dispatch subtypes (abstract or
    /// [FastClonerPolymorphic]) and are declared in this compilation, most derived first.
    /// </summary>
    private static List<INamedTypeSymbol> FindAllDispatchRoots(INamedTypeSymbol constructedSubtype)
    {
        List<INamedTypeSymbol> roots = [];
        INamedTypeSymbol? current = constructedSubtype.BaseType;
        while (current != null)
        {
            INamedTypeSymbol definition = current.OriginalDefinition;

            if (TypeAnalyzer.HasClonableAttribute(definition) &&
                (definition.IsAbstract || HasPolymorphicAttribute(definition)) &&
                definition.DeclaringSyntaxReferences.Length > 0)
            {
                roots.Add(definition);
            }

            current = current.BaseType;
        }

        return roots;
    }

    private static bool HasPolymorphicAttribute(INamedTypeSymbol type)
    {
        foreach (AttributeData attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerPolymorphicAttribute")
                return true;
        }
        return false;
    }
}
