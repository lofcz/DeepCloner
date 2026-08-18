using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Represents the member-level clone behavior as determined by attributes.
/// Mirrors FastCloner.Code.CloneBehavior enum values.
/// </summary>
internal enum MemberCloneBehavior
{
    Clone = 0,      // Default: deep clone
    Reference = 1,  // Copy reference directly
    Shallow = 2,    // MemberwiseClone (treated same as Reference for members)
    Ignore = 3      // Skip, set to default
}

internal record struct MemberAnalysis(MemberModel Model, ITypeSymbol Type);

internal static class MemberCollector
{
    public static List<MemberAnalysis> GetMembers(
        INamedTypeSymbol symbol,
        Compilation compilation,
        bool nullabilityEnabled,
        ExternalIgnoreRegistry externalIgnores)
    {
        List<MemberAnalysis> members = [];
        HashSet<string> seenNames = [];
        FastClonerMemberVisibility visibilityPolicy = GetVisibilityPolicyFromType(symbol);

        // Resolved external-ignore sets per declaring assembly (null = none registered).
        // Registry lookups are string-keyed; symbol-keyed memoization keeps it to one
        // lookup per distinct assembly in the inheritance chain.
        Dictionary<IAssemblySymbol, HashSet<string>?>? resolvedByAssembly = externalIgnores.IsEmpty
            ? null
            : new Dictionary<IAssemblySymbol, HashSet<string>?>(SymbolEqualityComparer.Default);

        HashSet<string>? ResolveExternalIgnores(IAssemblySymbol containing)
        {
            if (resolvedByAssembly == null)
                return null;

            if (!resolvedByAssembly.TryGetValue(containing, out HashSet<string>? resolved))
            {
                string displayName = containing.Identity.GetDisplayName();
                resolved = null;
                foreach (ExternalIgnoreAssemblyRegistration registration in externalIgnores.Registrations)
                {
                    if (registration.AssemblyName == displayName)
                    {
                        resolved = [.. registration.AttributeTypeFqns];
                        break;
                    }
                }

                resolvedByAssembly[containing] = resolved;
            }

            return resolved;
        }

        // Get all members from base types too (walking up the inheritance chain)
        INamedTypeSymbol? currentType = symbol;
        while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
        {
            HashSet<string>? assemblyExternalIgnores = ResolveExternalIgnores(currentType.ContainingAssembly);

            foreach (ISymbol? member in currentType.GetMembers())
            {
                if (member.IsStatic || member.IsImplicitlyDeclared)
                    continue;

                // Skip if we've already seen this member name (base class members override derived)
                if (!seenNames.Add(member.Name))
                    continue;

                if (member is IPropertySymbol property)
                {
                    if (property is { GetMethod: not null, IsIndexer: false })
                    {
                        bool isPopulatableCollection = IsPopulatableCollectionType(property.Type);
                        bool hasSetter = property.SetMethod != null;

                        if (hasSetter || isPopulatableCollection)
                        {
                            MemberCloneBehavior behavior = GetMemberBehavior(property, property.Type, compilation, assemblyExternalIgnores);
                            if (behavior == MemberCloneBehavior.Ignore)
                                continue;

                            if (!HasExplicitMemberBehaviorAttribute(property, compilation))
                            {
                                FastClonerMemberVisibility memberMask = PropertyVisibilityMask(property);
                                if ((visibilityPolicy & memberMask) == 0)
                                    continue;
                            }

                            if (IsRedundantNonAutoPropertyClonedThroughField(property, visibilityPolicy, compilation, assemblyExternalIgnores))
                                continue;

                            members.Add(new MemberAnalysis(MemberModel.Create(property, nullabilityEnabled, compilation, behavior), property.Type));
                        }
                    }
                }
                else if (member is IFieldSymbol field)
                {
                    if (field.IsConst) continue; // Skip const fields

                    MemberCloneBehavior behavior = GetMemberBehavior(field, field.Type, compilation, assemblyExternalIgnores);
                    if (behavior == MemberCloneBehavior.Ignore)
                        continue;

                    if (!HasExplicitMemberBehaviorAttribute(field, compilation))
                    {
                        FastClonerMemberVisibility memberMask = MemberModel.MapAccessibility(field.DeclaredAccessibility);
                        if ((visibilityPolicy & memberMask) == 0)
                            continue;
                    }

                    members.Add(new MemberAnalysis(MemberModel.Create(field, nullabilityEnabled, compilation, behavior), field.Type));
                }
            }

            currentType = currentType.BaseType;
        }

        return members;
    }
    
    private static FastClonerMemberVisibility GetVisibilityPolicyFromType(INamedTypeSymbol type)
    {
        INamedTypeSymbol? current = type;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (AttributeData attr in current.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != "FastCloner.Code.FastClonerVisibilityAttribute")
                    continue;

                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is int rawFlags)
                {
                    return (FastClonerMemberVisibility)rawFlags;
                }
            }

            current = current.BaseType;
        }

        return FastClonerMemberVisibility.All;
    }
    
    private static bool HasExplicitMemberBehaviorAttribute(ISymbol member, Compilation compilation)
    {
        INamedTypeSymbol? behaviorAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerBehaviorAttribute");
        if (behaviorAttribute == null)
            return false;

        foreach (AttributeData attr in member.GetAttributes())
        {
            INamedTypeSymbol? attrClass = attr.AttributeClass;
            while (attrClass != null)
            {
                if (SymbolEqualityComparer.Default.Equals(attrClass, behaviorAttribute))
                    return true;
                attrClass = attrClass.BaseType;
            }
        }

        return false;
    }
    
    private static FastClonerMemberVisibility PropertyVisibilityMask(IPropertySymbol property)
    {
        Accessibility get = property.GetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable;
        Accessibility set = property.SetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable;
        Accessibility chosen = MoreAccessible(get, set);
        if (chosen == Accessibility.NotApplicable)
            chosen = property.DeclaredAccessibility;
        return MemberModel.MapAccessibility(chosen);
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
    
    private static bool IsPopulatableCollectionType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol:
                return false;
            case INamedTypeSymbol { IsGenericType: true } namedType:
            {
                string originalDef = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
                // List of populatable collection types (can use Clear + Add pattern)
                string[] populatableTypes =
                [
                    "global::System.Collections.Generic.List<T>",
                    "global::System.Collections.Generic.HashSet<T>",
                    "global::System.Collections.Generic.LinkedList<T>",
                    "global::System.Collections.Generic.Queue<T>",
                    "global::System.Collections.Generic.Stack<T>",
                    "global::System.Collections.Generic.SortedSet<T>",
                    "global::System.Collections.ObjectModel.Collection<T>",
                    "global::System.Collections.ObjectModel.ObservableCollection<T>",
                    "global::System.Collections.Concurrent.ConcurrentBag<T>",
                    "global::System.Collections.Concurrent.ConcurrentQueue<T>",
                    "global::System.Collections.Concurrent.ConcurrentStack<T>",
                    // Dictionaries
                    "global::System.Collections.Generic.Dictionary<TKey, TValue>",
                    "global::System.Collections.Generic.SortedDictionary<TKey, TValue>",
                    "global::System.Collections.Generic.SortedList<TKey, TValue>",
                    "global::System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>"
                ];
            
                foreach (string populatable in populatableTypes)
                {
                    if (originalDef == populatable)
                        return true;
                }

                break;
            }
        }

        // Also check if the type implements ICollection<T> and has Add method
        // This covers custom collection types
        foreach (INamedTypeSymbol? iface in type.AllInterfaces)
        {
            if (iface.IsGenericType)
            {
                string ifaceDef = iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (ifaceDef == "global::System.Collections.Generic.ICollection<T>")
                {
                    // Has ICollection<T>, check if it's not read-only
                    // ICollection<T>.IsReadOnly would need runtime check, so we just allow it
                    // The generated code will handle runtime failures gracefully
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Gets the clone behavior for a type by checking for FastClonerBehaviorAttribute on the type definition.
    /// </summary>
    private static MemberCloneBehavior? GetTypeBehavior(ITypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? behaviorAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerBehaviorAttribute");
        INamedTypeSymbol? ignoreAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerIgnoreAttribute");
        INamedTypeSymbol? shallowAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerShallowAttribute");
        INamedTypeSymbol? referenceAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerReferenceAttribute");

        foreach (AttributeData attr in type.GetAttributes())
        {
            INamedTypeSymbol? attrClass = attr.AttributeClass;
            if (attrClass == null)
                continue;

            if (ignoreAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, ignoreAttribute))
            {
                return attr.ConstructorArguments.Length switch
                {
                    0 => MemberCloneBehavior.Ignore,
                    > 0 when attr.ConstructorArguments[0].Value is bool ignored => ignored ? MemberCloneBehavior.Ignore : null,
                    _ => MemberCloneBehavior.Ignore
                };
            }

            if (shallowAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, shallowAttribute))
            {
                return MemberCloneBehavior.Shallow;
            }

            if (referenceAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, referenceAttribute))
            {
                return MemberCloneBehavior.Reference;
            }

            if (behaviorAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, behaviorAttribute))
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int behaviorInt)
                {
                    return (MemberCloneBehavior)behaviorInt;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the clone behavior for a member by checking:
    /// 1. Member-level FastClonerBehaviorAttribute (highest priority)
    /// 2. [NonSerialized] attribute (treat as Ignore)
    /// 3. External ignore attributes registered for the declaring assembly via
    ///    [assembly: FastClonerExternalIgnore(...)] (treat as Ignore)
    /// 4. Type-level FastClonerBehaviorAttribute on the member's type (lowest priority)
    /// </summary>
    private static MemberCloneBehavior GetMemberBehavior(
        ISymbol member,
        ITypeSymbol memberType,
        Compilation compilation,
        HashSet<string>? externalIgnores)
    {
        // Get all relevant attribute types
        INamedTypeSymbol? behaviorAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerBehaviorAttribute");
        INamedTypeSymbol? ignoreAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerIgnoreAttribute");
        INamedTypeSymbol? shallowAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerShallowAttribute");
        INamedTypeSymbol? referenceAttribute = compilation.GetTypeByMetadataName("FastCloner.Code.FastClonerReferenceAttribute");
        INamedTypeSymbol? nonSerializedAttribute = compilation.GetTypeByMetadataName("System.NonSerializedAttribute");

        // 1. Check for member-level attributes first (highest priority)
        bool hasExternalIgnore = false;
        foreach (AttributeData attr in member.GetAttributes())
        {
            INamedTypeSymbol? attrClass = attr.AttributeClass;
            if (attrClass == null)
                continue;

            // Check for specific derived attributes first (shorthand attributes)
            if (ignoreAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, ignoreAttribute))
            {
                return attr.ConstructorArguments.Length switch
                {
                    // Check if Ignored property is true (default is true)
                    0 => MemberCloneBehavior.Ignore,
                    > 0 when attr.ConstructorArguments[0].Value is bool ignored => ignored ? MemberCloneBehavior.Ignore : MemberCloneBehavior.Clone,
                    _ => MemberCloneBehavior.Ignore
                };
            }

            if (shallowAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, shallowAttribute))
            {
                return MemberCloneBehavior.Shallow;
            }

            if (referenceAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, referenceAttribute))
            {
                return MemberCloneBehavior.Reference;
            }

            // Check for base FastClonerBehaviorAttribute with explicit behavior parameter
            if (behaviorAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, behaviorAttribute))
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int behaviorInt)
                {
                    return (MemberCloneBehavior)behaviorInt;
                }
            }

            // Check for [NonSerialized] - treat as Ignore
            if (nonSerializedAttribute != null && SymbolEqualityComparer.Default.Equals(attrClass, nonSerializedAttribute))
            {
                return MemberCloneBehavior.Ignore;
            }

            // Track external ignore matches without returning: explicit FastCloner attributes
            // elsewhere on the member must keep priority regardless of attribute ordering.
            if (externalIgnores != null && !hasExternalIgnore)
            {
                for (INamedTypeSymbol? candidate = attrClass;
                     candidate is { SpecialType: not SpecialType.System_Object };
                     candidate = candidate.BaseType)
                {
                    if (externalIgnores.Contains(candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    {
                        hasExternalIgnore = true;
                        break;
                    }
                }
            }
        }

        // 2. Registered external ignore attributes (e.g. JsonIgnore, BsonIgnore, NotMapped)
        if (hasExternalIgnore)
            return MemberCloneBehavior.Ignore;

        // 3. Check for type-level attribute on the member's type
        MemberCloneBehavior? typeBehavior = GetTypeBehavior(memberType, compilation);

        return typeBehavior ?? MemberCloneBehavior.Clone;
    }

    private static bool IsRedundantNonAutoPropertyClonedThroughField(
        IPropertySymbol property,
        FastClonerMemberVisibility visibilityPolicy,
        Compilation compilation,
        HashSet<string>? externalIgnores)
    {
        if (property.SetMethod == null)
            return false;
        Accessibility setterAccess = property.SetMethod.DeclaredAccessibility;
        bool setterIsAccessible = setterAccess is Accessibility.Public
                                                or Accessibility.Internal
                                                or Accessibility.ProtectedOrInternal;
        if (setterIsAccessible)
            return false;
        if (HasAutoPropertyBackingField(property))
            return false;

        IFieldSymbol? target = TryGetSimpleSetterTargetField(property, compilation);
        return target != null && WillFieldBeCollected(target, visibilityPolicy, compilation, externalIgnores);
    }
    
    private static IFieldSymbol? TryGetSimpleSetterTargetField(IPropertySymbol property, Compilation compilation)
    {
        IMethodSymbol? setter = property.SetMethod;
        if (setter == null)
            return null;

        foreach (SyntaxReference syntaxRef in setter.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not AccessorDeclarationSyntax accessor)
                continue;

            ExpressionSyntax? candidate = null;
            if (accessor.ExpressionBody != null)
            {
                candidate = accessor.ExpressionBody.Expression;
            }
            else if (accessor.Body is { Statements.Count: 1 } body
                     && body.Statements[0] is ExpressionStatementSyntax exprStmt)
            {
                candidate = exprStmt.Expression;
            }

            if (candidate is not AssignmentExpressionSyntax assign)
                continue;
            if (!assign.OperatorToken.IsKind(SyntaxKind.EqualsToken))
                continue;
            if (assign.Right is not IdentifierNameSyntax rhs || rhs.Identifier.Text != "value")
                continue;

            SemanticModel sm = compilation.GetSemanticModel(assign.SyntaxTree);
            ISymbol? lhsSymbol = sm.GetSymbolInfo(assign.Left).Symbol;
            if (lhsSymbol is IFieldSymbol field
                && SymbolEqualityComparer.Default.Equals(field.ContainingType, property.ContainingType))
            {
                return field;
            }
        }

        return null;
    }
    
    private static bool WillFieldBeCollected(
        IFieldSymbol field,
        FastClonerMemberVisibility visibilityPolicy,
        Compilation compilation,
        HashSet<string>? externalIgnores)
    {
        if (field.IsConst || field.IsStatic || field.IsImplicitlyDeclared)
            return false;

        MemberCloneBehavior behavior = GetMemberBehavior(field, field.Type, compilation, externalIgnores);
        if (behavior == MemberCloneBehavior.Ignore)
            return false;

        if (HasExplicitMemberBehaviorAttribute(field, compilation))
            return true;

        FastClonerMemberVisibility mask = MemberModel.MapAccessibility(field.DeclaredAccessibility);
        return (visibilityPolicy & mask) != 0;
    }
    
    private static bool HasAutoPropertyBackingField(IPropertySymbol property)
    {
        INamedTypeSymbol? container = property.ContainingType;
        if (container == null)
            return false;
        string backingFieldName = $"<{property.Name}>k__BackingField";
        foreach (ISymbol member in container.GetMembers(backingFieldName))
        {
            if (member is IFieldSymbol)
                return true;
        }
        return false;
    }
}

