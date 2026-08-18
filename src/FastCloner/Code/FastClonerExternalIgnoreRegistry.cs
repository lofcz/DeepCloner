using System.Collections.Concurrent;
using System.Reflection;

namespace FastCloner.Code;

/// <summary>
/// Resolves external ignore attribute types registered for an assembly via
/// <c>[assembly: FastClonerExternalIgnore(...)]</c>. Assembly attributes are immutable at runtime,
/// so resolved sets are cached statically and intentionally survive <see cref="FastClonerCache.ClearCache"/>.
/// </summary>
internal static class FastClonerExternalIgnoreRegistry
{
    private static readonly Type[] NoAttributes = [];

    private static readonly ConcurrentDictionary<Assembly, Type[]> cache = new();

    internal static Type[] GetForAssembly(Assembly assembly)
    {
        return cache.TryGetValue(assembly, out Type[]? existing)
            ? existing
            : cache.GetOrAdd(assembly, static asm => Build(asm));
    }

    private static Type[] Build(Assembly assembly)
    {
        try
        {
            object[] attributes = assembly.GetCustomAttributes(typeof(FastClonerExternalIgnoreAttribute), inherit: false);
            if (attributes.Length == 0)
                return NoAttributes;

            List<Type> registered = [];
            foreach (object attribute in attributes)
            {
                foreach (Type attributeType in ((FastClonerExternalIgnoreAttribute)attribute).AttributeTypes)
                {
                    if (!registered.Contains(attributeType))
                        registered.Add(attributeType);
                }
            }

            return registered.Count == 0 ? NoAttributes : registered.ToArray();
        }
        catch
        {
            // Unloadable attribute types etc. - treat as no registration
            return NoAttributes;
        }
    }

    /// <summary>
    /// Checks whether the member carries an attribute matching one of the registered types.
    /// Matching walks the attribute's base class chain, mirroring the source generator's
    /// symbol-based matching so both cloners behave identically.
    /// </summary>
    internal static bool Contains(Type[] registered, MemberInfo member)
    {
        if (registered.Length == 0)
            return false;

        object[] attributes = member.GetCustomAttributes(inherit: false);
        foreach (object attribute in attributes)
        {
            for (Type attributeType = attribute.GetType();
                 attributeType is not null && attributeType != typeof(object);
                 attributeType = attributeType.BaseType!)
            {
                foreach (Type registeredType in registered)
                {
                    if (registeredType == attributeType)
                        return true;
                }
            }
        }

        return false;
    }
}
