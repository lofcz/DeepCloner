using FastCloner.Code;

namespace FastCloner;

/// <summary>
/// Extensions for object cloning
/// </summary>
public static class FastCloner
{
    private static readonly object configSync = new();
    private static long nextConfigCacheKey;
    private static volatile bool disableOptionalFeatures;
    private static volatile int maxRecursionDepth = 1_000;
    private static FastClonerPublishedEngine publishedEngine = FastClonerPublishedEngine.StartupDefault;

    internal static FastClonerPublishedEngine GetPublishedEngine() => Volatile.Read(ref publishedEngine);
    internal static FastClonerRuntimeConfig GetRuntimeConfigSnapshot() => GetPublishedEngine().RuntimeConfig;

    internal static bool DisableOptionalFeatures
    {
        get => disableOptionalFeatures;
    }

    internal static void SetDisableOptionalFeatures(bool value)
    {
        lock (configSync)
        {
            if (disableOptionalFeatures == value)
                return;

            PublishEngine(CreatePublishedEngine(maxRecursionDepth, value, GetTypeBehaviors()));
        }
    }

    /// <summary>
    /// Cloning objects with nest level above this threshold uses iterative approach instead of recursion.
    /// </summary>
    public static int MaxRecursionDepth
    {
        get => maxRecursionDepth;
        set
        {
            lock (configSync)
            {
                if (maxRecursionDepth == value)
                    return;

                PublishMaxRecursionDepth(value);
            }
        }
    }
    
    /// <summary>
    /// Performs deep (full) copy of object and related graph
    /// </summary>
    public static T? DeepClone<T>(T? obj) => FastClonerGenerator.CloneObject(obj);

    /// <summary>
    /// Performs deep (full) copy of object and related graph to existing object
    /// </summary>
    /// <returns>existing filled object</returns>
    /// <remarks>Method is valid only for classes, classes should be descendants in reality, not in declaration</remarks>
    public static TTo? DeepCloneTo<TFrom, TTo>(TFrom? objFrom, TTo? objTo) where TTo : class, TFrom => (TTo?)FastClonerGenerator.CloneObjectTo(objFrom, objTo, true);

    /// <summary>
    /// Performs shallow copy of object to existing object
    /// </summary>
    /// <returns>existing filled object</returns>
    /// <remarks>Method is valid only for classes, classes should be descendants in reality, not in declaration</remarks>
    public static TTo? ShallowCloneTo<TFrom, TTo>(TFrom? objFrom, TTo? objTo) where TTo : class, TFrom => (TTo?)FastClonerGenerator.CloneObjectTo(objFrom, objTo, false);

    /// <summary>
    /// Performs shallow (only new object returned, without cloning of dependencies) copy of object
    /// </summary>
    public static T? ShallowClone<T>(T? obj) => ShallowClonerGenerator.CloneObject(obj);

    /// <summary>
    /// Clears all cached information about classes, structs, types, and other CLR objects.
    /// </summary>
    public static void ClearCache() => FastClonerCache.ClearCache();
    
    /// <summary>
    /// Sets the cloning behavior for a type.
    /// <list type="bullet">
    /// <item><description><see cref="CloneBehavior.Clone"/> - Default behavior, performs deep cloning (removes any custom behavior).</description></item>
    /// <item><description><see cref="CloneBehavior.Reference"/> - Returns the same instance without cloning (for immutable/safe types).</description></item>
    /// <item><description><see cref="CloneBehavior.Ignore"/> - Returns null/default and skips cloning entirely.</description></item>
    /// </list>
    /// </summary>
    /// <param name="type">The type to configure.</param>
    /// <param name="behavior">The cloning behavior to apply.</param>
    /// <remarks>
    /// Setting <see cref="CloneBehavior.Clone"/> removes any custom behavior (equivalent to <see cref="ClearTypeBehavior"/>).
    /// Note that changing behavior clears the cache, which may impact performance until the cache is repopulated.
    /// </remarks>
    public static void SetTypeBehavior(Type type, CloneBehavior behavior)
    {
        lock (configSync)
        {
            if (behavior == CloneBehavior.Clone)
            {
                if (!FastClonerCache.TypeBehaviors.TryRemove(type, out _))
                    return;
            }
            else if (FastClonerCache.TypeBehaviors.TryGetValue(type, out CloneBehavior currentBehavior) && currentBehavior == behavior)
            {
                return;
            }
            else
            {
                FastClonerCache.TypeBehaviors[type] = behavior;
            }

            PublishEngine(CreatePublishedEngine(maxRecursionDepth, disableOptionalFeatures, GetTypeBehaviors()));
        }
    }

    /// <summary>
    /// Sets the cloning behavior for a type.
    /// </summary>
    /// <typeparam name="T">The type to configure.</typeparam>
    /// <param name="behavior">The cloning behavior to apply.</param>
    public static void SetTypeBehavior<T>(CloneBehavior behavior) => SetTypeBehavior(typeof(T), behavior);

    /// <summary>
    /// Gets the configured cloning behavior for a type, or null if using default behavior.
    /// </summary>
    /// <param name="type">The type to query.</param>
    /// <returns>The configured behavior, or null if no custom behavior is set.</returns>
    public static CloneBehavior? GetTypeBehavior(Type type) => FastClonerCache.GetTypeBehavior(type);

    /// <summary>
    /// Gets the configured cloning behavior for a type, or null if using default behavior.
    /// </summary>
    /// <typeparam name="T">The type to query.</typeparam>
    /// <returns>The configured behavior, or null if no custom behavior is set.</returns>
    public static CloneBehavior? GetTypeBehavior<T>() => GetTypeBehavior(typeof(T));

    /// <summary>
    /// Returns all types with custom cloning behaviors configured.
    /// </summary>
    public static Dictionary<Type, CloneBehavior> GetTypeBehaviors()
    {
        return new Dictionary<Type, CloneBehavior>(FastClonerCache.TypeBehaviors);
    }

    /// <summary>
    /// Clears any custom cloning behavior for a type, returning it to default deep clone behavior.
    /// </summary>
    /// <param name="type">The type to clear.</param>
    /// <returns>True if a custom behavior was removed, false if none was set.</returns>
    /// <remarks>
    /// Note that this clears the cache, which may have negative impact on cloning performance until the cache is repopulated.
    /// </remarks>
    public static bool ClearTypeBehavior(Type type)
    {
        lock (configSync)
        {
            if (!FastClonerCache.TypeBehaviors.TryRemove(type, out _))
                return false;

            PublishEngine(CreatePublishedEngine(maxRecursionDepth, disableOptionalFeatures, GetTypeBehaviors()));
            return true;
        }
    }

    /// <summary>
    /// Clears any custom cloning behavior for a type, returning it to default deep clone behavior.
    /// </summary>
    /// <typeparam name="T">The type to clear.</typeparam>
    /// <returns>True if a custom behavior was removed, false if none was set.</returns>
    public static bool ClearTypeBehavior<T>() => ClearTypeBehavior(typeof(T));

    /// <summary>
    /// Clears all custom type behaviors, returning all types to default deep clone behavior.
    /// </summary>
    /// <remarks>
    /// Note that this clears the cache, which may have negative impact on cloning performance until the cache is repopulated.
    /// </remarks>
    public static void ClearAllTypeBehaviors()
    {
        lock (configSync)
        {
            if (FastClonerCache.TypeBehaviors.IsEmpty)
                return;

            FastClonerCache.TypeBehaviors.Clear();
            PublishEngine(CreatePublishedEngine(maxRecursionDepth, disableOptionalFeatures, new Dictionary<Type, CloneBehavior>()));
        }
    }

    #region External ignore attributes

    /// <summary>
    /// Registers an external attribute type as an "ignore" signal. Any member (field, property, or event)
    /// decorated with this attribute will be skipped during cloning (set to its default value), exactly like
    /// <see cref="FastClonerIgnoreAttribute"/>.
    /// </summary>
    /// <typeparam name="T">An attribute type such as JsonIgnoreAttribute, BsonIgnoreAttribute, NotMappedAttribute, etc.</typeparam>
    /// <remarks>
    /// This is matched on attribute <em>presence</em> only. Attributes with conditional constructors
    /// (e.g. <c>JsonIgnore(Condition = ...)</c>) are not evaluated; the member is always ignored while registered.
    /// Works in reflection mode. Source-generated cloners cannot observe runtime registration and are unaffected.
    /// Registering the same attribute type multiple times is idempotent.
    /// </remarks>
    public static void RegisterIgnoreAttribute<T>() where T : Attribute
        => RegisterIgnoreAttribute(typeof(T));

    /// <summary>
    /// Registers an external attribute type as an "ignore" signal. See <see cref="RegisterIgnoreAttribute{T}"/>.
    /// </summary>
    /// <param name="attributeType">The attribute type to recognize as an ignore signal. Must derive from <see cref="Attribute"/>.</param>
    public static void RegisterIgnoreAttribute(Type attributeType)
    {
        if (attributeType is null)
            throw new ArgumentNullException(nameof(attributeType));
        if (!typeof(Attribute).IsAssignableFrom(attributeType))
            throw new ArgumentException($"'{attributeType}' is not an Attribute type.", nameof(attributeType));

        lock (configSync)
        {
            FastClonerCache.ExternalIgnoreAttributes[attributeType] = 0;
            FastClonerCache.ClearCache();
        }
    }

    /// <summary>
    /// Clears all registered external ignore attribute types. Internal helper used by tests to reset global state;
    /// not part of the public API.
    /// </summary>
    internal static void ResetIgnoreAttributes()
    {
        lock (configSync)
        {
            FastClonerCache.ExternalIgnoreAttributes.Clear();
            FastClonerCache.ClearCache();
        }
    }

    #endregion

    private static void PublishEngine(FastClonerPublishedEngine engine)
    {
        maxRecursionDepth = engine.RuntimeConfig.MaxRecursionDepth;
        disableOptionalFeatures = engine.RuntimeConfig.DisableOptionalFeatures;
        FastClonerCache.RecalculateTypeBehaviorState();
        FastClonerCache.ClearCache();
        Volatile.Write(ref publishedEngine, engine);
    }
    
    private static void PublishMaxRecursionDepth(int value)
    {
        FastClonerPublishedEngine current = Volatile.Read(ref publishedEngine);
        FastClonerRuntimeConfig oldConfig = current.RuntimeConfig;
        FastClonerRuntimeConfig newConfig = FastClonerRuntimeConfig.Create(
            value,
            oldConfig.DisableOptionalFeatures,
            oldConfig.CloneTypeBehaviors(),
            oldConfig.CacheKey,
            useStartupDefaultSingleton: true);

        FastClonerPublishedEngine newEngine = ReferenceEquals(newConfig, FastClonerRuntimeConfig.Default)
            ? FastClonerPublishedEngine.StartupDefault
            : FastClonerPublishedEngine.CreateConfigured(newConfig);

        maxRecursionDepth = value;
        Volatile.Write(ref publishedEngine, newEngine);
    }

    private static FastClonerPublishedEngine CreatePublishedEngine(
        int maxRecursionDepth,
        bool disableOptionalFeatures,
        Dictionary<Type, CloneBehavior>? typeBehaviors)
    {
        long cacheKey = Interlocked.Increment(ref nextConfigCacheKey);
        FastClonerRuntimeConfig config = FastClonerRuntimeConfig.Create(
            maxRecursionDepth,
            disableOptionalFeatures,
            typeBehaviors,
            cacheKey,
            useStartupDefaultSingleton: true);
        return ReferenceEquals(config, FastClonerRuntimeConfig.Default)
            ? FastClonerPublishedEngine.StartupDefault
            : FastClonerPublishedEngine.CreateConfigured(config);
    }
}