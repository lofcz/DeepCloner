using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class IncludeAttributeCollector
{
    public static EquatableArray<GenericUsage> Collect(GeneratorAttributeSyntaxContext context, TargetFramework targetFramework, ExternalIgnoreRegistry externalIgnores, CancellationToken cancellationToken)
    {
        INamedTypeSymbol? symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol == null || !symbol.IsGenericType)
            return EquatableArray<GenericUsage>.Empty;
        
        // Ensure the symbol itself is clonable
        if (!TypeAnalyzer.HasClonableAttribute(symbol))
            return EquatableArray<GenericUsage>.Empty;

        List<GenericUsage> usages = [];
        Compilation compilation = context.SemanticModel.Compilation;
        // Attribute is on the class definition, so use its context for nullability
        bool nullability = context.SemanticModel.GetNullableContext(context.TargetNode.SpanStart).HasFlag(NullableContext.Enabled);

        foreach (AttributeData? attr in context.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() is "FastCloner.SourceGenerator.Shared.FastClonerIncludeAttribute")
            {
                // Get the types from the constructor arguments
                if (attr.ConstructorArguments.Length > 0)
                {
                    // params Type[] types -> it's a single argument which is an array
                    TypedConstant arg = attr.ConstructorArguments[0];
                    if (arg.Kind == TypedConstantKind.Array)
                    {
                        foreach (TypedConstant typeConstant in arg.Values)
                        {
                            if (typeConstant.Value is ITypeSymbol typeArg)
                            {
                                // Included SUBTYPES (StringRepo : Repo<string>, TypedRepo<int> : Repo<List<int>>)
                                // are dispatch targets handled by DerivedTypeCollector, not T-argument
                                // registrations; feeding them here would emit meaningless Cloner<T> branches.
                                if (typeArg is INamedTypeSymbol namedTypeArg &&
                                    DerivedTypeCollector.IsDerivedFrom(namedTypeArg, symbol))
                                {
                                    continue;
                                }

                                GenericUsage? usage = GenericTypeAnalyzer.Analyze(symbol, typeArg, compilation, nullability, targetFramework, externalIgnores);
                                if (usage.HasValue)
                                {
                                    usages.Add(usage.Value);
                                }
                            }
                        }
                    }
                }
            }
        }

        return new EquatableArray<GenericUsage>(usages.Distinct().ToArray());
    }
}
