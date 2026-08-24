using System.Text;

namespace FastCloner.SourceGenerator;

internal readonly record struct NonPublicAccessor(
    string MemberName,                  // user-facing C# member name (e.g. "privateField" or "PrivateSetterProperty")
    string AccessorMethodName,          // generated identifier in the extension class (e.g. "__fc_privateField")
    string DeclaringTypeFqn,            // FQN of the type that declares the member
    string MemberTypeFqn,               // FQN of the field/property type
    NonPublicAccessorStrategy Strategy, // Field, BackingField, or SetterMethod
    bool IsBackingFieldStorage,         // True if the runtime storage is a field (true for Field & BackingField, false for SetterMethod)
    bool DeclaringTypeIsStruct,         // For struct receivers we need 'this' to be passed by ref to mutate it
    bool RequiresGetterAccessor         // True when the property's getter is also non-public, so we must emit a separate UnsafeAccessor for it
);

internal static class NonPublicAccessorEmitter
{
    public static string GetFieldStorageName(NonPublicAccessor accessor) =>
        accessor.Strategy switch
        {
            NonPublicAccessorStrategy.Field => accessor.MemberName,
            NonPublicAccessorStrategy.BackingField => $"<{accessor.MemberName}>k__BackingField",
            _ => accessor.MemberName,
        };
    
    public static string GetSetterMethodName(NonPublicAccessor accessor) => $"set_{accessor.MemberName}";

    public static string GetGetterMethodName(NonPublicAccessor accessor) => $"get_{accessor.MemberName}";

    public static string GetGetterAccessorIdentifier(NonPublicAccessor accessor) => $"{accessor.AccessorMethodName}_get";

    public static NonPublicAccessor BuildFor(MemberModel member, string declaringTypeFqnFallback, bool declaringTypeIsStruct)
    {
        string declaringFqn = member.DeclaringTypeFullName ?? declaringTypeFqnFallback;
        string accessorMethodName = $"__fc_{Sanitize(member.Name)}";

        bool isBackingFieldStorage =
            member.AccessorStrategy is NonPublicAccessorStrategy.Field or NonPublicAccessorStrategy.BackingField;
        
        bool requiresGetterAccessor = !isBackingFieldStorage && !member.GetterIsAccessible;

        return new NonPublicAccessor(
            MemberName: member.Name,
            AccessorMethodName: accessorMethodName,
            DeclaringTypeFqn: declaringFqn,
            MemberTypeFqn: member.TypeFullName,
            Strategy: member.AccessorStrategy,
            IsBackingFieldStorage: isBackingFieldStorage,
            DeclaringTypeIsStruct: declaringTypeIsStruct,
            RequiresGetterAccessor: requiresGetterAccessor);
    }
    
    public static void WriteDeclarations(CloneGeneratorContext context, StringBuilder sb, string indent, bool insideNestedShell)
    {
        if (context.NonPublicAccessors.Count == 0)
            return;

        bool useUnsafeAccessor = context.TargetFramework >= TargetFramework.Net8;
        bool useRuntimeBridge = !useUnsafeAccessor && context.IsFastClonerAvailable && context.BridgeContract.IsAvailable;

        if (!useUnsafeAccessor && !useRuntimeBridge)
            return;
        
        string accessorMemberAccess = insideNestedShell ? "internal" : "private";

        sb.AppendLine();
        sb.AppendLine($"{indent}// FastCloner SG: non-public member accessors");

        foreach (NonPublicAccessor accessor in context.NonPublicAccessors)
        {
            if (useUnsafeAccessor)
            {
                WriteUnsafeAccessorDeclaration(sb, accessor, indent, accessorMemberAccess);
            }
            else
            {
                WriteRuntimeBridgeCacheDeclaration(context, sb, accessor, indent, accessorMemberAccess);
            }
        }
    }

    private static void WriteUnsafeAccessorDeclaration(StringBuilder sb, NonPublicAccessor accessor, string indent, string memberAccess)
    {
        string thisParam = accessor.DeclaringTypeIsStruct
            ? $"ref {accessor.DeclaringTypeFqn} instance"
            : $"{accessor.DeclaringTypeFqn} instance";

        if (accessor.IsBackingFieldStorage)
        {
            string fieldName = GetFieldStorageName(accessor);
            sb.AppendLine($"{indent}[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{fieldName}\")]");
            sb.AppendLine($"{indent}{memberAccess} static extern ref {accessor.MemberTypeFqn} {accessor.AccessorMethodName}({thisParam});");
            return;
        }

        string setterName = GetSetterMethodName(accessor);
        sb.AppendLine($"{indent}[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"{setterName}\")]");
        sb.AppendLine($"{indent}{memberAccess} static extern void {accessor.AccessorMethodName}({thisParam}, {accessor.MemberTypeFqn} value);");

        if (accessor.RequiresGetterAccessor)
        {
            string getterName = GetGetterMethodName(accessor);
            string getterAccessorId = GetGetterAccessorIdentifier(accessor);
            sb.AppendLine($"{indent}[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"{getterName}\")]");
            sb.AppendLine($"{indent}{memberAccess} static extern {accessor.MemberTypeFqn} {getterAccessorId}({thisParam});");
        }
    }

    private static void WriteRuntimeBridgeCacheDeclaration(CloneGeneratorContext context, StringBuilder sb, NonPublicAccessor accessor, string indent, string memberAccess)
    {
        string proxyFqn = "global::" + context.BridgeContract.ProxyTypeFullName;
        if (accessor.IsBackingFieldStorage)
        {
            string fieldName = GetFieldStorageName(accessor);
            sb.AppendLine(
                $"{indent}{memberAccess} static readonly global::System.Reflection.FieldInfo {accessor.AccessorMethodName}_FI = " +
                $"{proxyFqn}.ResolveDeclaredField(typeof({accessor.DeclaringTypeFqn}), \"{fieldName}\");");
        }
        else
        {
            sb.AppendLine(
                $"{indent}{memberAccess} static readonly global::System.Reflection.PropertyInfo {accessor.AccessorMethodName}_PI = " +
                $"{proxyFqn}.ResolveDeclaredProperty(typeof({accessor.DeclaringTypeFqn}), \"{accessor.MemberName}\");");
        }
    }
    
    public static bool CanCloneNonPublicMembers(CloneGeneratorContext context)
    {
        if (context.TargetFramework >= TargetFramework.Net8)
            return true;
        
        return context is { IsFastClonerAvailable: true, BridgeContract.IsAvailable: true };
    }
    
    public static void WriteCloneCall(
        CloneGeneratorContext context,
        StringBuilder sb,
        MemberModel member,
        string resultVar,
        string sourceVar,
        string stateVar,
        string indent)
    {
        string declaringFqnFallback = context.Model.FullyQualifiedName;
        bool declaringIsStruct = context.Model.IsStruct;
        NonPublicAccessor accessor = BuildFor(member, declaringFqnFallback, declaringIsStruct);
        accessor = context.RegisterNonPublicAccessor(accessor);

        if (context.TargetFramework >= TargetFramework.Net8)
        {
            WriteUnsafeAccessorCall(context, sb, member, accessor, resultVar, sourceVar, stateVar, indent);
        }
        else
        {
            WriteRuntimeBridgeCall(context, sb, member, accessor, resultVar, sourceVar, indent);
        }
    }

    private static void WriteUnsafeAccessorCall(
        CloneGeneratorContext context,
        StringBuilder sb,
        MemberModel member,
        NonPublicAccessor accessor,
        string resultVar,
        string sourceVar,
        string stateVar,
        string indent)
    {
        string structRefSource = accessor.DeclaringTypeIsStruct ? "ref " : string.Empty;
        string structRefTarget = accessor.DeclaringTypeIsStruct ? "ref " : string.Empty;
        string accessorPrefix = context.GetNonPublicAccessorPrefix();

        string readExpression;
        if (member.GetterIsAccessible)
        {
            readExpression = $"{sourceVar}.{member.Name}";
        }
        else if (accessor.IsBackingFieldStorage)
        {
            readExpression = $"{accessorPrefix}{accessor.AccessorMethodName}({structRefSource}{sourceVar})";
        }
        else
        {
            string getterAccessorId = GetGetterAccessorIdentifier(accessor);
            readExpression = $"{accessorPrefix}{getterAccessorId}({structRefSource}{sourceVar})";
        }

        string clonedExpression = ProduceClonedExpression(context, member, readExpression, stateVar);

        if (accessor.IsBackingFieldStorage)
        {
            sb.AppendLine($"{indent}{accessorPrefix}{accessor.AccessorMethodName}({structRefTarget}{resultVar}) = {clonedExpression};");
        }
        else
        {
            sb.AppendLine($"{indent}{accessorPrefix}{accessor.AccessorMethodName}({structRefTarget}{resultVar}, {clonedExpression});");
        }
    }

    private static void WriteRuntimeBridgeCall(
        CloneGeneratorContext context,
        StringBuilder sb,
        MemberModel member,
        NonPublicAccessor accessor,
        string resultVar,
        string sourceVar,
        string indent)
    {
        string accessorPrefix = context.GetNonPublicAccessorPrefix();
        string proxyFqn = "global::" + context.BridgeContract.ProxyTypeFullName;

        if (accessor.DeclaringTypeIsStruct)
        {
            string boxVar = $"__nptarget_{context.GetNextVariableId()}";
            string structFqn = context.Model.FullyQualifiedName;
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    object {boxVar} = (object){resultVar};");
            WriteRuntimeBridgeCallStatement(sb, indent + "    ", proxyFqn, accessorPrefix, accessor, member, sourceVar, boxVar);
            sb.AppendLine($"{indent}    {resultVar} = ({structFqn}){boxVar};");
            sb.AppendLine($"{indent}}}");
        }
        else
        {
            WriteRuntimeBridgeCallStatement(sb, indent, proxyFqn, accessorPrefix, accessor, member, sourceVar, resultVar);
        }
    }

    private static void WriteRuntimeBridgeCallStatement(
        StringBuilder sb,
        string indent,
        string proxyFqn,
        string accessorPrefix,
        NonPublicAccessor accessor,
        MemberModel member,
        string sourceVar,
        string targetVar)
    {
        if (accessor.IsBackingFieldStorage)
        {
            string fiRef = $"{accessorPrefix}{accessor.AccessorMethodName}_FI";
            string method = member.IsShallowClone ? "CopyField" : "DeepCloneField";
            sb.AppendLine($"{indent}{proxyFqn}.{method}({sourceVar}, {targetVar}, {fiRef});");
        }
        else
        {
            string piRef = $"{accessorPrefix}{accessor.AccessorMethodName}_PI";
            sb.AppendLine($"{indent}{proxyFqn}.DeepCloneProperty({sourceVar}, {targetVar}, {piRef});");
        }
    }

    private static string ProduceClonedExpression(CloneGeneratorContext context, MemberModel member, string readExpression, string stateVar)
    {
        // Null-forgiving operators: the accessor signatures drop nullability annotations, so a
        // legitimately nullable member value would otherwise raise CS8601/CS8604 in generated code.
        if (member.IsShallowClone)
            return $"{readExpression}!";

        switch (member.TypeKind)
        {
            case MemberTypeKind.Safe:
                return $"{readExpression}!";

            case MemberTypeKind.Clonable:
                return $"{member.ClonableExtensionClass}.InternalFastDeepClone({readExpression}, {stateVar})!";
            
            default:
                if (context.IsFastClonerAvailable)
                {
                    return $"({member.TypeFullName})({CloneGeneratorContext.FastClonerDeepCloneCall(readExpression)}!)";
                }
                return $"{readExpression}!";
        }
    }

    private static string Sanitize(string name)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}