using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

/// <summary>
/// A single assembly's registration of external ignore attribute types.
/// Assembly identity and attribute types are stored as strings so the record stays
/// equatable and safe to flow through the incremental pipeline.
/// </summary>
internal sealed record ExternalIgnoreAssemblyRegistration(
    string AssemblyName,
    EquatableArray<string> AttributeTypeFqns);

/// <summary>
/// All external ignore registrations visible in a compilation: the source assembly plus
/// referenced assemblies. Mirrors the reflection cloner, which resolves registrations from
/// each member's declaring assembly, so both cloners produce identical behavior.
/// </summary>
internal sealed record ExternalIgnoreRegistry(EquatableArray<ExternalIgnoreAssemblyRegistration> Registrations)
{
    public static readonly ExternalIgnoreRegistry Empty = new(EquatableArray<ExternalIgnoreAssemblyRegistration>.Empty);

    public bool IsEmpty => Registrations.Count == 0;
}

internal static class ExternalIgnoreCollector
{
    private const string RegistrationAttributeFqn = "FastCloner.Code.FastClonerExternalIgnoreAttribute";
    private static readonly SymbolDisplayFormat QualifiedNameFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static ExternalIgnoreRegistry Collect(Compilation compilation)
    {
        List<ExternalIgnoreAssemblyRegistration>? registrations = null;

        registrations = AddAssemblyRegistrations(registrations, compilation.Assembly);

        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            registrations = AddAssemblyRegistrations(registrations, referenced);
        }

        if (registrations is null)
            return ExternalIgnoreRegistry.Empty;

        // Sort for stable equality across collection-order variations
        ExternalIgnoreAssemblyRegistration[] sorted = registrations
            .OrderBy(r => r.AssemblyName, System.StringComparer.Ordinal)
            .ToArray();

        return new ExternalIgnoreRegistry(new EquatableArray<ExternalIgnoreAssemblyRegistration>(sorted));
    }

    private static List<ExternalIgnoreAssemblyRegistration>? AddAssemblyRegistrations(
        List<ExternalIgnoreAssemblyRegistration>? registrations,
        IAssemblySymbol assembly)
    {
        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != RegistrationAttributeFqn)
                continue;

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array)
            {
                continue;
            }

            List<string> fqns = [];
            foreach (TypedConstant typeConstant in attribute.ConstructorArguments[0].Values)
            {
                if (typeConstant.Value is INamedTypeSymbol attributeType)
                {
                    string fqn = attributeType.ToDisplayString(QualifiedNameFormat);
                    if (!fqns.Contains(fqn))
                        fqns.Add(fqn);
                }
            }

            if (fqns.Count > 0)
            {
                fqns.Sort(System.StringComparer.Ordinal);
                (registrations ??= []).Add(new ExternalIgnoreAssemblyRegistration(
                    assembly.Identity.GetDisplayName(),
                    new EquatableArray<string>(fqns.ToArray())));
            }
        }

        return registrations;
    }
}
