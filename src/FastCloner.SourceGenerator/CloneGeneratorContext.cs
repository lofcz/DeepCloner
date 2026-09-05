using System.Collections.Generic;
using System.Text;

namespace FastCloner.SourceGenerator;

internal sealed class CloneGeneratorContext
{
    public TypeModel Model { get; }
    public StringBuilder Source { get; } = new StringBuilder();
    
    private readonly Dictionary<string, string> _typeNameToMethodName;
    private readonly HashSet<string> _neededHelperMethods;
    private readonly Queue<string> _pendingHelperMethods = new Queue<string>();
    private readonly Dictionary<string, MemberModel> _typeNameToMemberModel = new Dictionary<string, MemberModel>();
    private readonly Dictionary<string, TypeModel> _implicitTypeModels = new Dictionary<string, TypeModel>();
    private readonly Dictionary<string, TypeModel> _derivedTypeHelpers = new Dictionary<string, TypeModel>();
    private readonly HashSet<string> _usedDerivedHelperMethodNames = new HashSet<string>();
    private readonly Dictionary<string, int> _helperUsageCounts = new Dictionary<string, int>();

    public bool NeedsStateClass { get; set; }
    public bool NeedsClonerClass { get; set; }
    public bool UseStaticMethods { get; set; } = true;
    
    public bool CanHaveCircularReferences { get; set; }
    public bool NeedsStateTracking { get; set; }
    public bool IsFastClonerAvailable { get; }
    public TargetFramework TargetFramework { get; }
    public BridgeContract BridgeContract { get; }
    public List<NonPublicAccessor> NonPublicAccessors { get; } = [];
    public List<string> SkippedNonPublicMembers { get; } = [];

    private readonly Dictionary<string, bool> _circularReferenceOverrides = new Dictionary<string, bool>();

    public CloneGeneratorContext(TypeModel model, BridgeContract? bridgeContract = null, Dictionary<string, string>? sharedMethodNames = null, HashSet<string>? sharedNeededHelpers = null)
    {
        Model = model;
        CanHaveCircularReferences = model.CanHaveCircularReferences;
        IsFastClonerAvailable = model.IsFastClonerAvailable;
        TargetFramework = model.TargetFramework;
        BridgeContract = bridgeContract ?? BridgeContract.Empty;
        
        bool anyMemberNeedsIdentity = false;
        foreach (MemberModel m in model.Members)
        {
            if (m.PreserveIdentity == true)
            {
                anyMemberNeedsIdentity = true;
                break;
            }
        }
        NeedsStateTracking = model.NeedsStateTracking || anyMemberNeedsIdentity;
        
        _typeNameToMethodName = sharedMethodNames ?? new Dictionary<string, string>();
        _neededHelperMethods = sharedNeededHelpers ?? [];

        foreach (TypeModel? related in model.RelatedTypes)
        {
            IndexTypeName(_implicitTypeModels, related.FullyQualifiedName, related, related.IsStruct);
        }
        
        foreach (MemberModel nested in model.NestedTypes)
        {
            IndexTypeName(_typeNameToMemberModel, nested.TypeFullName, nested, nested.IsValueType);
        }
    }

    public void SetCircularReferenceOverride(string typeName, bool needsState)
    {
        _circularReferenceOverrides[typeName] = needsState;
    }

    public bool NeedsCircularState(string typeName, bool defaultFromModel)
    {
        if (_circularReferenceOverrides.TryGetValue(typeName, out bool overrideValue))
        {
            return overrideValue;
        }
        return defaultFromModel;
    }

    public bool HasPendingHelperMethods => _pendingHelperMethods.Count > 0;

    public string DequeuePendingHelperMethod() => _pendingHelperMethods.Dequeue();

    public bool TryGetImplicitTypeModel(string typeName, out TypeModel model)
    {
        return _implicitTypeModels.TryGetValue(typeName, out model);
    }

    public bool TryGetMemberModel(string typeName, out MemberModel model)
    {
        return _typeNameToMemberModel.TryGetValue(typeName, out model);
    }

    public string GetMethodName(string typeName)
    {
        return _typeNameToMethodName[typeName];
    }

    public void RegisterImplicitType(TypeModel model)
    {
        IndexTypeName(_implicitTypeModels, model.FullyQualifiedName, model, model.IsStruct);
    }
    
    public void RegisterExternalMethod(string typeFullName, string methodName)
    {
        _typeNameToMethodName[typeFullName] = methodName;
    }
    
    public string GetOrCreateHelperMethodName(string typeFullName)
    {
        if (_typeNameToMethodName.TryGetValue(typeFullName, out string? existingMethod))
        {
            return existingMethod;
        }
        
        string methodName = $"FastClonerSgClone{GetCleanTypeName(typeFullName)}";
        bool isValueType = _implicitTypeModels.TryGetValue(typeFullName, out TypeModel implicitModel) && implicitModel.IsStruct;
        IndexTypeName(_typeNameToMethodName, typeFullName, methodName, isValueType);

        if (_neededHelperMethods.Add(typeFullName))
        {
            _pendingHelperMethods.Enqueue(typeFullName);
        }

        return methodName;
    }
    
    public string GetOrCreateHelperMethodName(MemberModel member)
    {
        string typeKey = member.TypeFullName;

        if (_typeNameToMethodName.TryGetValue(typeKey, out string? existingMethod))
        {
            return existingMethod;
        }
        
        string methodName = $"FastClonerSgClone{GetCleanTypeName(member.TypeFullName)}";
        IndexTypeName(_typeNameToMethodName, typeKey, methodName, member.IsValueType);

        if (_neededHelperMethods.Add(typeKey))
        {
            _pendingHelperMethods.Enqueue(typeKey);
        }
        
        IndexTypeName(_typeNameToMemberModel, typeKey, member, member.IsValueType);

        return methodName;
    }

    /// <summary>
    /// Element/key/value type names include the usage-site NRT suffix (<c>Payload?</c>),
    /// while helper keys are the underlying type (<c>Payload</c>). Index both so lookups match.
    /// </summary>
    private static void IndexTypeName<T>(Dictionary<string, T> map, string typeFullName, T value, bool isValueType)
    {
        if (!map.ContainsKey(typeFullName))
            map[typeFullName] = value;

        if (!isValueType && typeFullName.Length > 0 && typeFullName[typeFullName.Length - 1] != '?')
        {
            string annotated = typeFullName + "?";
            if (!map.ContainsKey(annotated))
                map[annotated] = value;
        }
    }
    
    private static string GetCleanTypeName(string typeName)
    {
        return typeName
            .Replace("global::", "")
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('.', '_')
            .Replace('[', '_')
            .Replace(']', '_')
            .Replace('?', '_')
            .Replace(':', '_');
    }
    
    /// <summary>
    /// Registers a private clone helper for a dispatched derived type and returns its method name.
    /// The name is uniquified when needed: two closed constructions of the same generic subtype
    /// (e.g. TypedRepo&lt;int&gt; and TypedRepo&lt;string&gt;) share the same simple name.
    /// </summary>
    public string RegisterDerivedTypeHelper(TypeModel derivedType, string baseMethodName)
    {
        if (!_derivedTypeHelpers.ContainsKey(derivedType.FullyQualifiedName))
        {
            string methodName = baseMethodName;
            int suffix = 2;
            while (!_usedDerivedHelperMethodNames.Add(methodName))
                methodName = $"{baseMethodName}_{suffix++}";

            _derivedTypeHelpers[derivedType.FullyQualifiedName] = derivedType;
            _typeNameToMethodName[derivedType.FullyQualifiedName] = methodName;
        }

        return _typeNameToMethodName[derivedType.FullyQualifiedName];
    }
    
    public IEnumerable<(TypeModel Model, string MethodName)> GetDerivedTypeHelpers()
    {
        foreach (KeyValuePair<string, TypeModel> kvp in _derivedTypeHelpers)
        {
            yield return (kvp.Value, _typeNameToMethodName[kvp.Key]);
        }
    }
    
    public bool HasDerivedTypeHelpers => _derivedTypeHelpers.Count > 0;

    public void IncrementHelperUsage(string typeFullName)
    {
        if (_helperUsageCounts.TryGetValue(typeFullName, out int count))
        {
            _helperUsageCounts[typeFullName] = count + 1;
        }
        else
        {
            _helperUsageCounts[typeFullName] = 1;
        }
    }

    public int GetHelperUsageCount(string typeFullName)
    {
        return _helperUsageCounts.TryGetValue(typeFullName, out int count) ? count : 0;
    }

    public bool ShouldInline(string typeFullName)
    {
        return GetHelperUsageCount(typeFullName) == 1;
    }

    private int variableCounter;
    public int GetNextVariableId() => System.Threading.Interlocked.Increment(ref variableCounter);
    
    public string GetNonPublicAccessorPrefix()
    {
        return Model.TypeParameters.Count == 0 ? string.Empty : $"__FcAccessors<{string.Join(", ", Model.TypeParameters)}>.";
    }

    public NonPublicAccessor RegisterNonPublicAccessor(NonPublicAccessor accessor)
    {
        foreach (NonPublicAccessor existing in NonPublicAccessors)
        {
            if (existing.AccessorMethodName == accessor.AccessorMethodName)
                return existing;
        }
        NonPublicAccessors.Add(accessor);
        return accessor;
    }

    public static string FastClonerDeepCloneCall(string expression) => $"global::FastCloner.FastCloner.DeepClone({expression})";
    
    public static string NotNullIfNotNullAttr(bool isAvailable, string paramName = "source") 
        => isAvailable 
            ? $"[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"{paramName}\")]" 
            : "";
}
