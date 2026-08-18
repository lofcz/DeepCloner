using System;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Represents a type model for code generation.
/// CRITICAL: This is a record for proper equality comparison to enable incremental caching.
/// It stores NO Roslyn symbols (ISymbol, Syntax nodes) as they break caching.
/// </summary>
internal sealed record TypeModel(
    string Namespace,
    string Name,
    string FullyQualifiedName,
    bool IsStruct,
    bool IsSealed,
    bool IsAbstract,
    bool IsRecord,
    bool HasClonableBaseClass,
    bool CanHaveCircularReferences,
    bool NeedsStateTracking, // True if state needed for cycles OR identity preservation
    bool IsFastClonerAvailable,
    EquatableArray<MemberModel> Members,
    EquatableArray<string> TypeParameters,
    EquatableArray<string> TypeConstraints,
    EquatableArray<TypeModel> RelatedTypes, // Implicitly clonable types that we generate helpers for
    EquatableArray<MemberModel> NestedTypes, // Nested collection types that need helpers
    EquatableArray<TypeModel> DerivedTypes, // Concrete derived types for abstract class dispatch
    bool NullabilityEnabled,
    bool TrustNullability, // Whether to trust nullability annotations and skip null checks
    bool? PreserveIdentity = null, // null=default (off), true=preserve identity in subgraph, false=explicitly disabled
    bool IsRefLikeType = false, // Whether the type is a ref struct (cannot be boxed/used as generic)
    bool HasParameterlessConstructor = true, // Whether the type has a public parameterless constructor (defaults to true for safety)
    bool CodeAnalysisAvailable = false, // Whether System.Diagnostics.CodeAnalysis attributes are available
    bool IsPolymorphicRoot = false, // Non-abstract root marked [FastClonerPolymorphic]: dispatch by runtime type to subtype cloners
    TargetFramework TargetFramework = TargetFramework.NetStandard20, // Detected target framework for TFM-specific optimizations
    EquatableArray<string> CircularAnalysisLog = default) : IEquatable<TypeModel>;
