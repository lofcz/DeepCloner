using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Cacheable snapshot of a [FastClonerPolymorphic] usage for validation diagnostics.
/// Stores no Roslyn symbols or syntax nodes so incremental caching stays intact.
/// </summary>
internal sealed record PolymorphicValidationInfo(
    string TypeName,
    bool HasClonableAttribute,
    bool IsAbstract,
    bool IsSealed)
{
    public static PolymorphicValidationInfo FromSymbol(INamedTypeSymbol? symbol)
    {
        return new PolymorphicValidationInfo(
            symbol?.Name ?? "?",
            symbol != null && TypeAnalyzer.HasClonableAttribute(symbol),
            symbol?.IsAbstract ?? false,
            symbol?.IsSealed ?? false);
    }

    public void Report(SourceProductionContext context)
    {
        if (!HasClonableAttribute)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "FCG012",
                    "FastClonerPolymorphic without FastClonerClonable",
                    "'[FastClonerPolymorphic]' is applied to '{0}', which is not marked with '[FastClonerClonable]'. " +
                    "Polymorphic cloning requires the type to be clonable; the attribute has no effect.",
                    "FastCloner",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true),
                Location.None,
                TypeName));
            return;
        }

        if (IsSealed)
        {
            // Static classes are both abstract and sealed in metadata.
            string reason = IsAbstract ? "a static class" : "sealed";
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "FCG011",
                    "FastClonerPolymorphic on type that cannot have subtypes",
                    $"'[FastClonerPolymorphic]' is applied to '{{0}}', which is {reason} and cannot have subtypes. The attribute has no effect.",
                    "FastCloner",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true),
                Location.None,
                TypeName));
            return;
        }

        // Abstract types dispatch by runtime type already; the attribute is accepted
        // there as explicit self-documentation and needs no diagnostic. Generic roots
        // are supported: closed constructions of subtypes are collected and dispatched.
    }
}
