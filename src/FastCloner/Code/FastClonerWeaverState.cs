using System.Collections.Concurrent;
using System.Reflection;

namespace FastCloner.Code;

/// <summary>
/// Detection of weaver-injected runtime state on instrumented types (IL weavers such as PostSharp,
/// Metalama and similar transform compiled assemblies AFTER the compiler has run).
///
/// Weavers inject members that are invisible in source code and initialized only by code the weaver
/// adds to constructors. The most prominent example is PostSharp's family of instance-scoped aspects
/// (e.g. <c>[NotifyPropertyChanged]</c>): the weaver adds a private field holding the per-instance
/// aspect runtime state, assigns it in every constructor, rewrites property accessors to dispatch
/// through it, and re-initializes it in a woven <see cref="object.MemberwiseClone"/> replacement
/// (ICloneAwareAspect / AspectInitializationReason.Clone).
///
/// Such state is not part of the type's data:
///  - deep-cloning it duplicates subscriber tables, weak references and native handles that were
///    never designed to be cloned, which surfaces as NullReferenceException inside weaver-generated
///    code (https://github.com/lofcz/FastCloner/issues/48);
///  - resetting it to default leaves woven accessors dereferencing null, which fails the same way;
///  - copying the reference (what <see cref="object.MemberwiseClone"/> does) is the only safe
///    operation available without weaver cooperation.
/// </summary>
internal static class FastClonerWeaverState
{
    private static readonly ConcurrentDictionary<FieldInfo, bool> WeaverStateFieldCache = new();

    private static readonly ConcurrentDictionary<Type, MethodInfo?> DeclaredMemberwiseCloneCache = new();

    internal static bool IsWeaverStateField(FieldInfo field)
    {
        return WeaverStateFieldCache.GetOrAdd(field, CheckIsWeaverStateField);
    }

    private static bool CheckIsWeaverStateField(FieldInfo field)
    {
        // PostSharp prefixes every member it introduces with '~' (e.g. ~postsharp~field~1).
        // '~' cannot appear in C#, F# or VB identifiers, so this cannot misfire on user-declared fields.
        if (field.Name.IndexOf('~') >= 0)
        {
            return true;
        }

        // PostSharp/Metalama aspect instances are attribute objects (every aspect derives from
        // System.Attribute), and user code essentially never declares instance fields typed as
        // attributes, so an attribute-typed field is weaver state.
        return typeof(Attribute).IsAssignableFrom(field.FieldType);
    }

    /// <summary>
    /// Returns the <c>MemberwiseClone()</c> method declared on the type itself, if any. Weavers
    /// (PostSharp in particular) replace MemberwiseClone on instrumented types so that the copy
    /// step also re-initializes weaver state on the clone; calling it when present produces a
    /// correctly instrumented clone instead of a raw field copy.
    /// </summary>
    internal static MethodInfo? GetDeclaredMemberwiseCloneMethod(Type type)
    {
        return DeclaredMemberwiseCloneCache.GetOrAdd(type, FindDeclaredMemberwiseClone);
    }

    private static MethodInfo? FindDeclaredMemberwiseClone(Type type)
    {
        // System.Object itself declares MemberwiseClone; that is not a weaver hook.
        if (type == typeof(object))
        {
            return null;
        }

        try
        {
            MethodInfo? method = type.GetMethod(
                nameof(MemberwiseClone),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            return method != null && typeof(object).IsAssignableFrom(method.ReturnType)
                ? method
                : null;
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }
    }
}
