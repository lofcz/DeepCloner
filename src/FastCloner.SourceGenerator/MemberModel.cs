using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

[Flags]
internal enum FastClonerMemberVisibility
{
    None = 0,
    Public = 1,
    Internal = 2,
    Protected = 4,
    Private = 8,
    NonPublic = Internal | Protected | Private,
    All = Public | NonPublic,
}

internal enum MemberTypeKind
{
    Safe,           // Primitives, strings - can be shallow copied
    Clonable,       // Has [FastClonerClonable] attribute
    Collection,     // List<T>, IEnumerable<T>, etc.
    Dictionary,     // Dictionary<K,V>
    Array,          // T[] (single-dimensional)
    MultiDimArray,  // T[,], T[,,], etc. (multi-dimensional arrays)
    Object,         // System.Object
    Implicit,       // Implicitly clonable (struct/class with all clonable members)
    Other           // Everything else - shallow copy fallback
}

internal enum NonPublicAccessorStrategy
{
    None = 0,
    Field = 1,
    BackingField = 2,
    SetterMethod = 3
}

internal enum CollectionKind
{
    None,
    List,
    HashSet,
    Queue,
    Stack,
    LinkedList,
    SortedSet,
    ConcurrentQueue,
    ConcurrentStack,
    ConcurrentBag,
    Dictionary,
    SortedDictionary,
    SortedList,
    ConcurrentDictionary,
    ObservableCollection,
    ReadOnlyCollection,
    ReadOnlyDictionary,
    // Immutable collections
    ImmutableList,
    ImmutableArray,
    ImmutableHashSet,
    ImmutableSortedSet,
    ImmutableQueue,
    ImmutableStack,
    ImmutableDictionary,
    ImmutableSortedDictionary
}

/// <summary>
/// Represents a member (property or field) model for code generation.
/// This is a record struct for proper equality comparison in incremental generation.
/// All type analysis is pre-computed during creation to avoid needing Compilation later.
/// </summary>
internal readonly record struct MemberModel(
    string Name,
    string TypeFullName,
    bool IsReadOnly,
    bool IsProperty,
    bool IsField,
    // Pre-analyzed type characteristics for code generation
    MemberTypeKind TypeKind,
    string? ElementTypeName,         // For collections/arrays
    string? KeyTypeName,             // For dictionaries
    string? ValueTypeName,           // For dictionaries
    bool ElementIsSafe,              // For optimization - can skip deep cloning
    bool ElementHasClonableAttr,     // For optimization - call FastDeepClone
    bool KeyIsSafe,                  // For dictionary key optimization
    bool KeyIsClonable,              // For dictionary key optimization
    bool ValueIsSafe,                // For dictionary value optimization
    bool ValueIsClonable,            // For dictionary value optimization
    bool RequiresFastCloner,         // If true, this member requires FastCloner library to be cloned correctly
    CollectionKind CollectionKind,   // Specific kind of collection (List, Set, Queue, etc.)
    string? ConcreteTypeFullName,    // Concrete type to instantiate (if interface/abstract)
    bool IsValueType,                // Whether the member type is a value type (struct)
    bool IsInitOnly,                 // Whether the property has init accessor (requires object initializer)
    bool IsRequired,                 // Whether the member is required (must be in object initializer)
    int ArrayRank,                   // For multi-dimensional arrays: the rank (2 for T[,], 3 for T[,,], etc.)
    bool IsNullable,                 // Whether the member is explicitly nullable (annotated with ?)
    // Property accessor capabilities
    bool HasGetter,                  // Whether the property has a getter
    bool HasSetter,                  // Whether the property has a setter (regular, not init-only)
    bool SetterIsAccessible,         // Whether the setter is publicly accessible (not private/protected)
    MemberCloneBehavior MemberBehavior,  // The clone behavior for this member (Clone, Reference, Shallow, Ignore)
    bool? PreserveIdentity = null,   // null=inherit from type, true=preserve identity for this member's subgraph, false=disabled
    bool CollectionHasCount = true,  // Whether the source collection type has Count property
    bool CollectionHasIndexer = true, // Whether the source collection type supports [i] indexing
    string? ClonableExtensionClass = null,         // Precomputed FQN of the extension class for this type (when TypeKind == Clonable)
    string? ElementClonableExtensionClass = null,   // Precomputed FQN of the extension class for the element type (when ElementHasClonableAttr)
    FastClonerMemberVisibility MemberVisibility = FastClonerMemberVisibility.Public, // Visibility mask of the member, used by the type-level [FastClonerVisibility] policy
    NonPublicAccessorStrategy AccessorStrategy = NonPublicAccessorStrategy.None,  // How to access the member when it is not publicly accessible
    string? DeclaringTypeFullName = null,  // FQN of the type that DECLARES this member (may differ from the cloned type for inherited members)
    bool GetterIsAccessible = true, // Whether the value can be read directly via source.X (false => read via accessor too)
    bool IsWeaverState = false,     // Weaver-injected runtime state (PostSharp aspects and the like): not data, never deep-cloned
    bool HasBackingFieldStorage = false, // Property is backed by a compiler-generated <Name>k__BackingField
    // Verified capabilities of the concrete collection type generated code instantiates (see CollectionCapabilities).
    // Never assumed from CollectionKind: Collection<T>, BindingList<T> etc. share the List kind but not List's API (issue #50).
    bool ConcreteIsList = false,          // Concrete type is exactly List<T>, so CollectionsMarshal is valid
    bool ConcreteHasCapacityCtor = false, // Concrete type has a public .ctor(int capacity)
    bool ConcreteHasCopyCtor = false      // Concrete type has a ctor verified to COPY the source collection
) : IEquatable<MemberModel>
{
    /// <summary>
    /// Returns true if the member should have its reference copied directly without deep cloning.
    /// This applies to both Shallow and Reference behaviors.
    /// </summary>
    private bool ShouldCopyReference => MemberBehavior is MemberCloneBehavior.Shallow or MemberCloneBehavior.Reference;
    
    // Legacy property for backward compatibility
    public bool IsShallowClone => ShouldCopyReference;

    public static MemberModel Create(IPropertySymbol property, bool nullabilityEnabled, Compilation compilation, MemberCloneBehavior memberBehavior = MemberCloneBehavior.Clone)
    {
        (MemberTypeKind typeKind, string? elementName, string? keyName, string? valueName, bool elementSafe, bool elementClonable, bool keySafe, bool keyClonable, bool valSafe, bool valClonable, bool requiresFastCloner, CollectionKind collectionKind, string? concreteType, int arrayRank, bool collHasCount, bool collHasIndexer, string? clonableExtClass, string? elemClonableExtClass, bool concreteIsList, bool concreteHasCapacityCtor, bool concreteHasCopyCtor) 
            = AnalyzeType(property.Type, compilation);
        
        // Check if the property has an init-only setter (C# 9+)
        bool isInitOnly = property.SetMethod?.IsInitOnly ?? false;
        
        // Property accessor capabilities
        bool hasGetter = property.GetMethod != null;
        bool hasSetter = property.SetMethod != null && !isInitOnly;
        bool setterIsAccessible = property.SetMethod != null &&
                                  (property.SetMethod.DeclaredAccessibility == Accessibility.Public ||
                                   property.SetMethod.DeclaredAccessibility == Accessibility.Internal ||
                                   property.SetMethod.DeclaredAccessibility == Accessibility.ProtectedOrInternal);
        
        // Check nullability
        bool isNullable = property.NullableAnnotation == NullableAnnotation.Annotated;
        
        // Check for PreserveIdentity attribute
        bool? preserveIdentity = GetPreserveIdentityFromAttributes(property.GetAttributes());

        // Visibility mask: take the most permissive of getter/setter, mirroring the runtime mask helper.
        FastClonerMemberVisibility visibility = MapAccessibility(GetEffectivePropertyAccessibility(property));
        bool getterIsAccessible = property.GetMethod == null || IsAccessibleFromExternalClass(property.GetMethod.DeclaredAccessibility);

        // Accessor strategy: only relevant when the SETTER is non-public (we always need to write into the clone target).
        NonPublicAccessorStrategy accessorStrategy = NonPublicAccessorStrategy.None;
        if (property.SetMethod != null && !setterIsAccessible)
        {
            accessorStrategy = HasAutoPropertyBackingField(property)
                ? NonPublicAccessorStrategy.BackingField
                : NonPublicAccessorStrategy.SetterMethod;
        }
        else if (property.SetMethod == null && property.GetMethod != null && !getterIsAccessible)
        {
            // Truly read-only & non-public property; we can't clone it via direct assignment.
        }

        string? declaringTypeFqn = property.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool hasBackingFieldStorage = property.SetMethod != null && HasAutoPropertyBackingField(property);

        return new MemberModel(
            property.Name,
            property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            property.SetMethod == null || property.IsReadOnly,
            true,
            false,
            typeKind,
            elementName,
            keyName,
            valueName,
            elementSafe,
            elementClonable,
            keySafe,
            keyClonable,
            valSafe,
            valClonable,
            requiresFastCloner,
            collectionKind,
            concreteType,
            property.Type.IsValueType,
            isInitOnly,
            property.IsRequired,
            arrayRank,
            isNullable,
            hasGetter,
            hasSetter,
            setterIsAccessible,
            memberBehavior,
            preserveIdentity,
            collHasCount,
            collHasIndexer,
            clonableExtClass,
            elemClonableExtClass,
            visibility,
            accessorStrategy,
            declaringTypeFqn,
            getterIsAccessible,
            IsWeaverState: false,
            hasBackingFieldStorage,
            ConcreteIsList: concreteIsList,
            ConcreteHasCapacityCtor: concreteHasCapacityCtor,
            ConcreteHasCopyCtor: concreteHasCopyCtor);
    }

    public static MemberModel Create(IFieldSymbol field, bool nullabilityEnabled, Compilation compilation, MemberCloneBehavior memberBehavior = MemberCloneBehavior.Clone)
    {
        (MemberTypeKind typeKind, string? elementName, string? keyName, string? valueName, bool elementSafe, bool elementClonable, bool keySafe, bool keyClonable, bool valSafe, bool valClonable, bool requiresFastCloner, CollectionKind collectionKind, string? concreteType, int arrayRank, bool collHasCount, bool collHasIndexer, string? clonableExtClass, string? elemClonableExtClass, bool concreteIsList, bool concreteHasCapacityCtor, bool concreteHasCopyCtor) 
            = AnalyzeType(field.Type, compilation);
        
        // Check nullability
        bool isNullable = field.NullableAnnotation == NullableAnnotation.Annotated;
        
        // Field accessor capabilities - fields always have getters, setters depend on readonly
        bool hasGetter = true;
        bool hasSetter = !field.IsReadOnly;
        bool fieldIsAccessible = IsAccessibleFromExternalClass(field.DeclaredAccessibility);
        bool setterIsAccessible = !field.IsReadOnly && fieldIsAccessible;
        
        // Check for PreserveIdentity attribute
        bool? preserveIdentity = GetPreserveIdentityFromAttributes(field.GetAttributes());

        FastClonerMemberVisibility visibility = MapAccessibility(field.DeclaredAccessibility);
        NonPublicAccessorStrategy accessorStrategy = fieldIsAccessible
            ? NonPublicAccessorStrategy.None
            : NonPublicAccessorStrategy.Field;
        string? declaringTypeFqn = field.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Weaver-injected state (PostSharp aspect instances and similar) is detected even for
        // source-declared fields; an explicit member behavior attribute opts the member back in.
        bool isWeaverState = memberBehavior == MemberCloneBehavior.Clone && DetectWeaverStateField(field);

        return new MemberModel(
            field.Name,
            field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            field.IsReadOnly,
            false,
            true,
            typeKind,
            elementName,
            keyName,
            valueName,
            elementSafe,
            elementClonable,
            keySafe,
            keyClonable,
            valSafe,
            valClonable,
            requiresFastCloner,
            collectionKind,
            concreteType,
            field.Type.IsValueType,
            false,  // Fields don't have init-only semantics
            field.IsRequired,
            arrayRank,
            isNullable,
            hasGetter,
            hasSetter,
            setterIsAccessible,
            memberBehavior,
            preserveIdentity,
            collHasCount,
            collHasIndexer,
            clonableExtClass,
            elemClonableExtClass,
            visibility,
            accessorStrategy,
            declaringTypeFqn,
            fieldIsAccessible,
            isWeaverState,
            ConcreteIsList: concreteIsList,
            ConcreteHasCapacityCtor: concreteHasCapacityCtor,
            ConcreteHasCopyCtor: concreteHasCopyCtor);
    }
    
    private static bool IsAccessibleFromExternalClass(Accessibility accessibility) =>
        accessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;
    
    private static Accessibility GetEffectivePropertyAccessibility(IPropertySymbol property)
    {
        Accessibility get = property.GetMethod?.DeclaredAccessibility ?? Accessibility.Private;
        Accessibility set = property.SetMethod?.DeclaredAccessibility ?? Accessibility.Private;
        return MoreAccessible(get, set);
    }

    private static Accessibility MoreAccessible(Accessibility a, Accessibility b)
    {
        return Score(a) >= Score(b) ? a : b;
        static int Score(Accessibility ac) => ac switch
        {
            Accessibility.Public => 6,
            Accessibility.ProtectedOrInternal => 5,
            Accessibility.Internal => 4,
            Accessibility.Protected => 3,
            Accessibility.ProtectedAndInternal => 2,
            Accessibility.Private => 1,
            _ => 0,
        };
    }

    public static FastClonerMemberVisibility MapAccessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => FastClonerMemberVisibility.Public,
            Accessibility.Internal => FastClonerMemberVisibility.Internal,
            Accessibility.Protected => FastClonerMemberVisibility.Protected,
            Accessibility.ProtectedOrInternal => FastClonerMemberVisibility.Protected | FastClonerMemberVisibility.Internal,
            Accessibility.ProtectedAndInternal => FastClonerMemberVisibility.Protected | FastClonerMemberVisibility.Internal,
            _ => FastClonerMemberVisibility.Private,
        };
    
    private static bool HasAutoPropertyBackingField(IPropertySymbol property)
    {
        INamedTypeSymbol? container = property.ContainingType;
        if (container == null) return false;
        string backingFieldName = $"<{property.Name}>k__BackingField";
        foreach (ISymbol member in container.GetMembers(backingFieldName))
        {
            if (member is IFieldSymbol)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mirrors FastCloner.Code.FastClonerWeaverState for source-declared fields: weavers such as
    /// PostSharp inject state fields named with '~' (impossible in C# identifiers) and aspect
    /// instances are attribute objects. Such fields are runtime state, not data.
    /// </summary>
    private static bool DetectWeaverStateField(IFieldSymbol field)
    {
        if (field.Name.Contains("~"))
            return true;

        for (INamedTypeSymbol? current = field.Type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current is { Name: "Attribute", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
                return true;
        }

        return false;
    }
    
    private static bool? GetPreserveIdentityFromAttributes(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (AttributeData attr in attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() == "FastCloner.SourceGenerator.Shared.FastClonerPreserveIdentityAttribute")
            {
                // Check named argument first (Enabled = true/false)
                foreach (KeyValuePair<string, TypedConstant> namedArg in attr.NamedArguments)
                {
                    if (namedArg is { Key: "Enabled", Value.Value: bool namedEnabled })
                        return namedEnabled;
                }
                
                // Check constructor argument [FastClonerPreserveIdentity(true/false)]
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is bool enabled)
                    return enabled;
                
                // Default value is true when attribute is present with no arguments
                return true;
            }
        }
        return null; // No attribute found
    }
    
    private static (MemberTypeKind kind, string? elem, string? key, string? val, bool elemSafe, bool elemClon, bool keySafe, bool keyClon, bool valSafe, bool valClon, bool requiresFastCloner, CollectionKind collKind, string? concreteType, int arrayRank, bool collHasCount, bool collHasIndexer, string? clonableExtClass, string? elemClonableExtClass, bool concreteIsList, bool concreteHasCapacityCtor, bool concreteHasCopyCtor) 
        AnalyzeType(ITypeSymbol type, Compilation compilation)
    {
        // Check if safe type (primitives, strings, etc.)
        if (TypeAnalyzer.IsSafeType(type, compilation))
            return (MemberTypeKind.Safe, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
        
        // Check if this is a "do not clone" type (delegates, Lazy, Task, etc.)
        // These are treated as Safe to prevent deep cloning (shallow copy semantics)
        if (TypeAnalyzer.IsDoNotCloneType(type))
            return (MemberTypeKind.Safe, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
        
        // Check if this is a ref struct type (Span<T>, ReadOnlySpan<T>, etc.)
        // Ref structs cannot be boxed and cannot be used with state tracking dictionary.
        // Treat them as Safe to use shallow copy (which is the correct semantics for ref structs anyway).
        if (TypeAnalyzer.IsRefStructType(type))
            return (MemberTypeKind.Safe, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
        
        // Check if has clonable attribute
        if (TypeAnalyzer.HasClonableAttribute(type))
            return (MemberTypeKind.Clonable, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, TypeAnalyzer.ComputeExtensionClassFqn(type), null, false, false, false);
        
        // Check if System.Object or Type Parameter (generic T)
        // For generics, we don't know at compile time if it's clonable.
        // We generate a smart fallback that handles safe types at runtime.
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == Microsoft.CodeAnalysis.TypeKind.TypeParameter)
            return (MemberTypeKind.Object, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
        
        // IMPORTANT: Check array BEFORE collection (arrays implement ICollection<T>)
        if (type is IArrayTypeSymbol arrayType)
        {
            string elemName = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool elemSafe = TypeAnalyzer.IsSafeType(arrayType.ElementType, compilation);
            bool elemClon = TypeAnalyzer.HasClonableAttribute(arrayType.ElementType);
            int rank = arrayType.Rank;
            
            // Multi-dimensional arrays (int[,], int[,,], etc.)
            if (rank > 1)
            {
                // Multi-dimensional arrays: if element is not safe and not clonable, we need FastCloner to deep clone elements
                bool requiresFastCloner = !elemSafe && !elemClon;
                string? elemExtClass = elemClon ? TypeAnalyzer.ComputeExtensionClassFqn(arrayType.ElementType) : null;
                return (MemberTypeKind.MultiDimArray, elemName, null, null, elemSafe, elemClon, false, false, false, false, requiresFastCloner, CollectionKind.None, null, rank, true, true, null, elemExtClass, false, false, false);
            }
            
            // Single-dimensional arrays: if element is not safe and not clonable, we need FastCloner to deep clone it
            bool requiresFastClonerSingle = !elemSafe && !elemClon;
            string? elemExtClassSingle = elemClon ? TypeAnalyzer.ComputeExtensionClassFqn(arrayType.ElementType) : null;
            return (MemberTypeKind.Array, elemName, null, null, elemSafe, elemClon, false, false, false, false, requiresFastClonerSingle, CollectionKind.None, null, 1, true, true, null, elemExtClassSingle, false, false, false);
        }
        
        // Check dictionary BEFORE collection (dictionaries implement ICollection<KeyValuePair<K,V>>)
        if (TypeAnalyzer.IsDictionaryType(type))
        {
            (ITypeSymbol KeyType, ITypeSymbol ValueType)? dictTypes = TypeAnalyzer.GetDictionaryTypes(type, compilation);
            if (dictTypes.HasValue)
            {
                CollectionKind collKind = TypeAnalyzer.GetCollectionKind(type);
                CollectionCapabilities caps = TypeAnalyzer.GetCollectionCapabilities(type, collKind, compilation, isDictionary: true);

                // A helper method would assume an unverified constructor/API surface: let the runtime cloner handle it.
                if (!TypeAnalyzer.CanGenerateDictionaryHelper(type, collKind, caps))
                    return (MemberTypeKind.Other, null, null, null, false, false, false, false, false, false, true, CollectionKind.None, null, 0, true, true, null, null, false, false, false);

                string keyName = dictTypes.Value.KeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string valName = dictTypes.Value.ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                
                bool keySafe = TypeAnalyzer.IsSafeType(dictTypes.Value.KeyType, compilation);
                bool keyClon = TypeAnalyzer.HasClonableAttribute(dictTypes.Value.KeyType);
                bool valSafe = TypeAnalyzer.IsSafeType(dictTypes.Value.ValueType, compilation);
                bool valClon = TypeAnalyzer.HasClonableAttribute(dictTypes.Value.ValueType);
                
                // If key or value is not safe/clonable, we might need FastCloner
                bool requiresFastCloner = (!keySafe && !keyClon) || (!valSafe && !valClon);
                
                string concreteType = TypeAnalyzer.GetConcreteTypeForCollection(type, collKind, $"{keyName}, {valName}");

                return (MemberTypeKind.Dictionary, null, keyName, valName, false, false, keySafe, keyClon, valSafe, valClon, requiresFastCloner, collKind, concreteType, 0, true, true, null, null, caps.IsExactList, caps.HasCapacityCtor, caps.HasCopyCtor);
            }
        }
        
        // Check collection (must be after array and dictionary)
        if (TypeAnalyzer.IsCollectionType(type))
        {
            ITypeSymbol? elemType = TypeAnalyzer.GetCollectionElementType(type, compilation);
            string? elemName = elemType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool elemSafe = elemType != null && TypeAnalyzer.IsSafeType(elemType, compilation);
            bool elemClon = elemType != null && TypeAnalyzer.HasClonableAttribute(elemType);
            // If element is not safe and not clonable, we need FastCloner
            bool requiresFastCloner = !elemSafe && !elemClon;
            
            CollectionKind collKind = TypeAnalyzer.GetCollectionKind(type);
            bool collHasCount = TypeAnalyzer.CollectionHasCountProperty(type);
            bool collHasIndexer = TypeAnalyzer.CollectionHasIndexer(type);
            CollectionCapabilities caps = TypeAnalyzer.GetCollectionCapabilities(type, collKind, compilation, isDictionary: false);

            // A helper method would assume an unverified constructor/API surface: let the runtime cloner handle it.
            if (!TypeAnalyzer.CanGenerateCollectionHelper(type, collKind, caps, collHasCount))
                return (MemberTypeKind.Other, null, null, null, false, false, false, false, false, false, true, CollectionKind.None, null, 0, true, true, null, null, false, false, false);

            string concreteType = TypeAnalyzer.GetConcreteTypeForCollection(type, collKind, elemName!);
            string? elemExtClass = elemClon && elemType != null ? TypeAnalyzer.ComputeExtensionClassFqn(elemType) : null;
            
            return (MemberTypeKind.Collection, elemName, null, null, elemSafe, elemClon, false, false, false, false, requiresFastCloner, collKind, concreteType, 0, collHasCount, collHasIndexer, null, elemExtClass, caps.IsExactList, caps.HasCapacityCtor, caps.HasCopyCtor);
        }

        // Check for implicit candidate (must be after collection)
        if (TypeAnalyzer.IsImplicitCandidate(type))
        {
            // It's a candidate for implicit cloning (generated recursively)
            return (MemberTypeKind.Implicit, null, null, null, false, false, false, false, false, false, false, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
        }
        
        // Everything else - shallow copy fallback
        // If it's "Other", it's an unknown type. We definitely need FastCloner to deep clone it.
        return (MemberTypeKind.Other, null, null, null, false, false, false, false, false, false, true, CollectionKind.None, null, 0, true, true, null, null, false, false, false);
    }
}
