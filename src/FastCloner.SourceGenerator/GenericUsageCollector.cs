using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastCloner.SourceGenerator;

internal static class GenericUsageCollector
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is GenericNameSyntax;
    }

    public static EquatableArray<GenericUsage> Collect(GeneratorSyntaxContext context, TargetFramework targetFramework, ExternalIgnoreRegistry externalIgnores, CancellationToken cancellationToken)
    {
        GenericNameSyntax node = (GenericNameSyntax)context.Node;
        INamedTypeSymbol? symbol = context.SemanticModel.GetSymbolInfo(node, cancellationToken).Symbol as INamedTypeSymbol;

        if (symbol == null || !symbol.IsGenericType)
            return EquatableArray<GenericUsage>.Empty;

        // Check if the generic type definition has the FastClonerClonable attribute
        // We check the OriginalDefinition because 'symbol' here is the concrete type (e.g. MyGeneric<int>)
        if (!TypeAnalyzer.HasClonableAttribute(symbol.OriginalDefinition))
            return EquatableArray<GenericUsage>.Empty;

        List<GenericUsage> usages = [];
        Compilation compilation = context.SemanticModel.Compilation;

        // Nullability context at usage site
        bool nullability = context.SemanticModel.GetNullableContext(node.SpanStart).HasFlag(NullableContext.Enabled);

        foreach (ITypeSymbol? typeArg in symbol.TypeArguments)
        {
            GenericUsage? usage = GenericTypeAnalyzer.Analyze(symbol, typeArg, compilation, nullability, targetFramework, externalIgnores);
            if (usage.HasValue)
            {
                usages.Add(usage.Value);
            }
        }

        return new EquatableArray<GenericUsage>(usages.ToArray());
    }
}
