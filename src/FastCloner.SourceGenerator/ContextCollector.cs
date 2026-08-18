using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class ContextCollector
{
    public static Result<ContextModel> Collect(GeneratorAttributeSyntaxContext context, TargetFramework targetFramework, ExternalIgnoreRegistry externalIgnores, System.Threading.CancellationToken cancellationToken)
    {
        INamedTypeSymbol? symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol == null)
        {
            return Result<ContextModel>.Error(Diagnostic.Create(
                new DiagnosticDescriptor("FCG005", "Invalid Symbol", "Symbol is not a named type", "FastCloner", DiagnosticSeverity.Error, true),
                context.TargetNode.GetLocation()));
        }

        // Verify inheritance from FastClonerContext
        INamedTypeSymbol? baseType = symbol.BaseType;
        bool inheritsFromContext = false;
        while (baseType != null)
        {
            if (baseType.Name == "FastClonerContext" && 
                baseType.ContainingNamespace.ToDisplayString() == "FastCloner.SourceGenerator.Shared")
            {
                inheritsFromContext = true;
                break;
            }
            baseType = baseType.BaseType;
        }

        if (!inheritsFromContext)
        {
             return Result<ContextModel>.Error(Diagnostic.Create(
                new DiagnosticDescriptor("FCG006", "Invalid Base Class", "Class must inherit from FastClonerContext", "FastCloner", DiagnosticSeverity.Error, true),
                symbol.Locations.FirstOrDefault() ?? Location.None));
        }

        List<TypeModel> registeredTypes = [];
        Compilation compilation = context.SemanticModel.Compilation;
        bool nullabilityEnabled = context.SemanticModel.GetNullableContext(context.TargetNode.SpanStart)
                        .HasFlag(NullableContext.Enabled);
        
        // Cache to prevent re-analyzing same types in recursion (shared across this context analysis)
        Dictionary<ITypeSymbol, TypeModel?> implicitCache = new Dictionary<ITypeSymbol, TypeModel?>(SymbolEqualityComparer.Default);
        HashSet<ITypeSymbol> processingStack = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (AttributeData? attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerRegisterAttribute")
            {
                if (attr.ConstructorArguments.Length > 0)
                {
                    TypedConstant typesArg = attr.ConstructorArguments[0];
                    List<ITypeSymbol> typesToProcess = [];

                    if (typesArg.Kind == TypedConstantKind.Array)
                    {
                        foreach (TypedConstant typedConst in typesArg.Values)
                        {
                            if (typedConst.Value is ITypeSymbol type)
                            {
                                typesToProcess.Add(type);
                            }
                        }
                    }
                    else if (typesArg.Value is ITypeSymbol type)
                    {
                        typesToProcess.Add(type);
                    }

                    foreach (ITypeSymbol? typeToRegister in typesToProcess)
                    {
                        if (ImplicitTypeAnalyzer.TryAnalyze(typeToRegister, compilation, nullabilityEnabled, targetFramework, externalIgnores, implicitCache, processingStack, out TypeModel? model))
                        {
                            if (model != null)
                            {
                                registeredTypes.Add(model);
                                // Also add related types!
                                foreach (TypeModel? rel in model.RelatedTypes)
                                {
                                    registeredTypes.Add(rel);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Deduplicate
        TypeModel[] uniqueTypes = registeredTypes.GroupBy(t => t.FullyQualifiedName).Select(g => g.First()).ToArray();

        // Check if FastCloner library is available (same check as TypeModelFactory)
        bool isFastClonerAvailable = compilation.GetTypeByMetadataName("FastCloner.FastCloner") != null;
        
        // Check if type has SimulateNoRuntime attribute (for testing)
        bool hasSimulateNoRuntime = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerSimulateNoRuntimeAttribute");
        
        if (hasSimulateNoRuntime)
        {
            isFastClonerAvailable = false;
        }

        // Check if System.Diagnostics.CodeAnalysis attributes are available
        bool codeAnalysisAvailable = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null;

        return Result<ContextModel>.Success(new ContextModel(
            symbol.Name,
            TypeAnalyzer.GetNamespace(symbol),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<TypeModel>(uniqueTypes),
            isFastClonerAvailable,
            codeAnalysisAvailable
        ));
    }
}
