using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FastCloner.SourceGenerator;

internal static class MemberCloneGenerator
{
    public static string GetMemberAssignment(CloneGeneratorContext context, MemberModel member, string sourceVar, string stateVar, string indent = "            ")
    {
        // Object-initializer assignments always run against constructor-created instances.
        // Weaver state is not data: the constructor already produced correct fresh state and
        // copying the source's state over it would corrupt weaver-generated code (issue #48).
        if (member.IsWeaverState)
            return string.Empty;

        string memberName = member.Name;
        string nf = (!member.IsNullable && !member.IsValueType) ? "!" : "";
        
        if (member.AccessorStrategy != NonPublicAccessorStrategy.None)
            return string.Empty;

        switch (member)
        {
            case { IsProperty: false, IsReadOnly: true }:
            case { IsProperty: true, HasGetter: true, HasSetter: false, IsInitOnly: false }:
                return string.Empty;
            default:
                if (member.IsShallowClone)
                {
                    return $"{memberName} = {sourceVar}.{memberName}";
                }
                
                switch (member.TypeKind)
                {
                    case MemberTypeKind.Safe:
                    {
                        return $"{memberName} = {sourceVar}.{memberName}";
                    }
                    case MemberTypeKind.Clonable:
                    {
                        string extensionClassName = GetExtensionClassName(member);
                        return $"{memberName} = {extensionClassName}.InternalFastDeepClone({sourceVar}.{memberName}, {stateVar}){nf}";
                    }
                    case MemberTypeKind.Implicit:
                    {
                        if (context.ShouldInline(member.TypeFullName) && 
                            context.TryGetImplicitTypeModel(member.TypeFullName, out var implicitModel))
                        {
                            bool inlineModelDefault = implicitModel.NeedsStateTracking;
                            bool inlineMemberNeedsState = context.NeedsCircularState(member.TypeFullName, inlineModelDefault) && context.NeedsStateTracking;
                            bool inlineShouldPassState = inlineMemberNeedsState || (stateVar != "null");
                            string inlineActualStateVar = inlineShouldPassState ? stateVar : "null";

                            return $"{memberName} = {GetImplicitCloneExpression(context, implicitModel, $"{sourceVar}.{memberName}", inlineActualStateVar, indent, member.IsNullable)}{nf}";
                        }

                        string helperMethodName = context.GetOrCreateHelperMethodName(member);
                        context.TryGetImplicitTypeModel(member.TypeFullName, out implicitModel);

                        bool modelDefault = implicitModel?.NeedsStateTracking ?? false;
                        bool memberNeedsState = context.NeedsCircularState(member.TypeFullName, modelDefault) && context.NeedsStateTracking;
                        bool isRegisteredType = helperMethodName == "Clone";
                        bool shouldPassState = memberNeedsState || (isRegisteredType && stateVar != "null");
                        string actualStateVar = shouldPassState ? stateVar : "null";

                        return $"{memberName} = {GetHelperMethodCall(context, helperMethodName, $"{sourceVar}.{memberName}", shouldPassState, actualStateVar)}{nf}";
                    }
                    case MemberTypeKind.Collection:
                    case MemberTypeKind.Dictionary:
                    case MemberTypeKind.Array:
                    case MemberTypeKind.MultiDimArray:
                    {
                        string helperMethodName = context.GetOrCreateHelperMethodName(member);
                        bool memberNeedsState = MemberNeedsCircularRefTracking(context, member);
                        string actualStateVar = memberNeedsState ? stateVar : "null";
                        
                        return $"{memberName} = {GetHelperMethodCall(context, helperMethodName, $"{sourceVar}.{memberName}", memberNeedsState, actualStateVar)}{nf}";
                    }
                    case MemberTypeKind.Object:
                    case MemberTypeKind.Other:
                    default:
                        context.NeedsClonerClass = true;
                        return $"{memberName} = Cloner<{member.TypeFullName}>.Clone({sourceVar}.{memberName}, {stateVar}){nf}";
                }
        }
    }

    /// <summary>
    /// Adjusts how a member is populated depending on how the target instance was created
    /// (see https://github.com/lofcz/FastCloner/issues/48 — PostSharp's INotifyPropertyChanged
    /// aspect and weavers with the same shape).
    /// </summary>
    /// <param name="member">Member to adapt; replaced with an adjusted copy when needed.</param>
    /// <param name="instanceCreatedWithoutConstructor">
    /// True when the target instance was created via GetUninitializedObject: no constructor ran,
    /// so weaver-injected state (PostSharp aspect instances and the like) is missing and property
    /// setters may dereference it.
    /// </param>
    /// <returns>False when the member must not be populated at all.</returns>
    private static bool AdaptMemberForPopulation(ref MemberModel member, bool instanceCreatedWithoutConstructor)
    {
        if (member.IsWeaverState)
        {
            if (!instanceCreatedWithoutConstructor)
            {
                // Constructor-created instance: the constructor already initialized fresh weaver
                // state; copying anything over it (deep clone or reference) corrupts it.
                return false;
            }

            // Uninitialized instance: the state field would otherwise stay null and crash woven
            // accessors. Sharing the source's state is the best portable approximation — exactly
            // what Object.MemberwiseClone produces for instrumented types.
            member = member with { MemberBehavior = MemberCloneBehavior.Reference };
            return true;
        }

        if (instanceCreatedWithoutConstructor && member is { IsProperty: true, HasGetter: true }
            && (member.HasSetter || member.IsInitOnly))
        {
            // Never invoke property setters (or init accessors) on an instance that skipped its
            // constructors: weavers rewrite accessors to dereference ctor-initialized state, which
            // throws NullReferenceException inside weaver code. Auto-property storage is written
            // directly instead; properties without resolvable storage are skipped — their backing
            // fields, when collected as members, still carry the data.
            if (!member.HasBackingFieldStorage)
                return false;

            member = member with { AccessorStrategy = NonPublicAccessorStrategy.BackingField };
        }

        return true;
    }

    public static void WriteMemberCloning(CloneGeneratorContext context, MemberModel member, string resultVar, string sourceVar, string stateVar, bool instanceCreatedWithoutConstructor = false)
    {
        if (!AdaptMemberForPopulation(ref member, instanceCreatedWithoutConstructor))
            return;

        string memberName = member.Name;
        string nf = (!member.IsNullable && !member.IsValueType) ? "!" : "";
        StringBuilder sb = context.Source;
        
        if (member.AccessorStrategy != NonPublicAccessorStrategy.None)
        {
            if (!NonPublicAccessorEmitter.CanCloneNonPublicMembers(context))
            {
                context.SkippedNonPublicMembers.Add(memberName);
                return;
            }

            NonPublicAccessorEmitter.WriteCloneCall(context, sb, member, resultVar, sourceVar, stateVar, "            ");
            return;
        }

        switch (member)
        {
            case { IsProperty: false, IsReadOnly: true }:
            case { IsProperty: true, IsInitOnly: true }:
                return;
            case { IsProperty: true, HasGetter: true, HasSetter: false, IsInitOnly: false }:
                WriteGetterOnlyCollectionPopulation(context, member, resultVar, sourceVar, stateVar);
                return;
            default:
                if (member.IsShallowClone)
                {
                    sb.AppendLine($"            {resultVar}.{memberName} = {sourceVar}.{memberName};");
                    return;
                }
                
                switch (member.TypeKind)
                {
                    case MemberTypeKind.Safe:
                        sb.AppendLine($"            {resultVar}.{memberName} = {sourceVar}.{memberName};");
                        break;

                    case MemberTypeKind.Clonable:
                    {
                        string extensionClassName = GetExtensionClassName(member);
                        sb.AppendLine($"            {resultVar}.{memberName} = {extensionClassName}.InternalFastDeepClone({sourceVar}.{memberName}, {stateVar}){nf};");
                    }
                        break;

                    case MemberTypeKind.Implicit:
                    {
                        if (context.ShouldInline(member.TypeFullName) && 
                            context.TryGetImplicitTypeModel(member.TypeFullName, out var implicitModel))
                        {
                            bool inlineModelDefault = implicitModel.NeedsStateTracking;
                            bool inlineMemberNeedsState = context.NeedsCircularState(member.TypeFullName, inlineModelDefault) && context.NeedsStateTracking;
                            bool inlineShouldPassState = inlineMemberNeedsState || (stateVar != "null");
                            string inlineActualStateVar = inlineShouldPassState ? stateVar : "null";

                            sb.AppendLine(GetImplicitCloneStatement(context, implicitModel, member.Name, resultVar, sourceVar, inlineActualStateVar, member.IsNullable));
                            break;
                        }

                        string helperMethodName = context.GetOrCreateHelperMethodName(member);
                        context.TryGetImplicitTypeModel(member.TypeFullName, out implicitModel);
                        bool modelDefault = implicitModel?.NeedsStateTracking ?? false;
                        bool memberNeedsState = context.NeedsCircularState(member.TypeFullName, modelDefault) && context.NeedsStateTracking;
                        bool isRegisteredType = helperMethodName == "Clone";
                        bool shouldPassState = memberNeedsState || (isRegisteredType && stateVar != "null");
                        string actualStateVar = shouldPassState ? stateVar : "null";
                
                        sb.AppendLine($"            {resultVar}.{memberName} = {GetHelperMethodCall(context, helperMethodName, $"{sourceVar}.{memberName}", shouldPassState, actualStateVar)}{nf};");
                    }
                        break;

                    case MemberTypeKind.Collection:
                    case MemberTypeKind.Dictionary:
                    case MemberTypeKind.Array:
                    case MemberTypeKind.MultiDimArray:
                    {
                        string helperMethodName = context.GetOrCreateHelperMethodName(member);
                        bool memberNeedsState = MemberNeedsCircularRefTracking(context, member);
                        string actualStateVar = memberNeedsState ? stateVar : "null";
                        sb.AppendLine($"            {resultVar}.{memberName} = {GetHelperMethodCall(context, helperMethodName, $"{sourceVar}.{memberName}", memberNeedsState, actualStateVar)}{nf};");
                    }
                        break;

                    case MemberTypeKind.Object:
                    case MemberTypeKind.Other:
                    default:
                        context.NeedsClonerClass = true;
                        sb.AppendLine($"            {resultVar}.{memberName} = Cloner<{member.TypeFullName}>.Clone({sourceVar}.{memberName}, {stateVar}){nf};");
                        break;
                }

                break;
        }
    }
    
    private static void WriteGetterOnlyCollectionPopulation(CloneGeneratorContext context, MemberModel member, string resultVar, string sourceVar, string stateVar)
    {
        StringBuilder sb = context.Source;
        string memberName = member.Name;
        
        if (member.TypeKind != MemberTypeKind.Collection && member.TypeKind != MemberTypeKind.Dictionary)
            return;

        string targetVar = $"target_{context.GetNextVariableId()}";
        sb.AppendLine($"            var {targetVar} = {resultVar}.{memberName};");
        sb.AppendLine($"            if ({sourceVar}.{memberName} != null && {targetVar} != null)");
        sb.AppendLine("            {");
        sb.AppendLine($"                {targetVar}.Clear();");
        
        if (member.IsShallowClone)
        {
            if (member.TypeKind == MemberTypeKind.Dictionary)
            {
                sb.AppendLine($"                foreach (var kvp in {sourceVar}.{memberName})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {targetVar}[kvp.Key!] = kvp.Value!;");
                sb.AppendLine("                }");
            }
            else
            {
                string addMethod = GetAddMethodForCollection(member.CollectionKind);
                sb.AppendLine($"                foreach (var item in {sourceVar}.{memberName})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {targetVar}.{addMethod}(item!);");
                sb.AppendLine("                }");
            }
        }
        else
        {
            string helperMethodName = context.GetOrCreateHelperMethodName(member);
            bool memberNeedsState = MemberNeedsCircularRefTracking(context, member);
            string actualStateVar = memberNeedsState ? stateVar : "null";
            string clonedVar = $"cloned_{context.GetNextVariableId()}";
            string helperCall = GetHelperMethodCall(context, helperMethodName, $"{sourceVar}.{memberName}", memberNeedsState, actualStateVar);
            sb.AppendLine($"                var {clonedVar} = {helperCall};");
            sb.AppendLine($"                if ({clonedVar} != null)");
            sb.AppendLine("                {");
            
            if (member.TypeKind == MemberTypeKind.Dictionary)
            {
                sb.AppendLine($"                    foreach (var kvp in {clonedVar})");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        {targetVar}[kvp.Key!] = kvp.Value!;");
                sb.AppendLine("                    }");
            }
            else
            {
                string addMethod = GetAddMethodForCollection(member.CollectionKind);
                sb.AppendLine($"                    foreach (var item in {clonedVar})");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        {targetVar}.{addMethod}(item!);");
                sb.AppendLine("                    }");
            }
            
            sb.AppendLine("                }");
        }
        
        sb.AppendLine("            }");
    }
    
    private static string GetAddMethodForCollection(CollectionKind kind)
    {
        return kind switch
        {
            CollectionKind.Queue or CollectionKind.ConcurrentQueue => "Enqueue",
            CollectionKind.Stack or CollectionKind.ConcurrentStack => "Push",
            CollectionKind.LinkedList => "AddLast",
            _ => "Add"
        };
    }
    
    public static bool MemberNeedsCircularRefTracking(CloneGeneratorContext context, MemberModel member)
    {
        if (member.PreserveIdentity is not null)
        {
            return member.PreserveIdentity.Value;
        }
        
        if (!context.NeedsStateTracking)
            return false;

        switch (member.TypeKind)
        {
            case MemberTypeKind.Safe:
                return false;
            case MemberTypeKind.Clonable:
                return true;
            case MemberTypeKind.Collection:
            case MemberTypeKind.Array:
            case MemberTypeKind.MultiDimArray:
            {
                if (member.ElementIsSafe)
                    return false;
                if (member.ElementHasClonableAttr)
                    return true;
                break;
            }
        }
        
        return true;
    }

    private static string GetImplicitCloneStatement(CloneGeneratorContext context, TypeModel implicitModel, string memberName, string resultVar, string sourceVar, string stateVar, bool isMemberNullable)
    {
        StringBuilder sb = new StringBuilder();
        string sourceProp = $"{sourceVar}.{memberName}";
        string safeName = $"l_{memberName}_{context.GetNextVariableId()}";

        sb.AppendLine($"            var {safeName} = {sourceProp};");

        bool skipNullCheck = context.Model.TrustNullability && !isMemberNullable;

        if (!implicitModel.IsStruct && !skipNullCheck)
        {
            sb.AppendLine($"            if ({safeName} != null)");
            sb.AppendLine("            {");
        }

        string typeName = implicitModel.FullyQualifiedName;
        string assignmentIndent = !implicitModel.IsStruct && !skipNullCheck ? "                    " : "                ";
        
        if (!implicitModel.IsStruct && !skipNullCheck)
        {
            sb.AppendLine($"                {resultVar}.{memberName} = new {typeName}");
            sb.AppendLine("                {");
        }
        else
        {
            sb.AppendLine($"            {resultVar}.{memberName} = new {typeName}");
            sb.AppendLine("            {");
        }
        
        List<string> assignments = [];
        
        foreach (MemberModel member in implicitModel.Members)
        {
             string assign = GetMemberAssignment(context, member, safeName, stateVar, assignmentIndent);
             if (!string.IsNullOrEmpty(assign))
             {
                 assignments.Add($"{assignmentIndent}{assign}");
             }
        }
        
        if (assignments.Count > 0)
        {
            sb.AppendLine(string.Join(",\n", assignments));
        }
        
        if (!implicitModel.IsStruct && !skipNullCheck)
        {
            sb.AppendLine("                };");
        }
        else
        {
            sb.AppendLine("            };");
        }
        
        if (!implicitModel.IsStruct && !skipNullCheck)
        {
            sb.AppendLine("            }");
        }
        
        return sb.ToString().TrimEnd();
    }

    internal static string GetImplicitCloneExpression(CloneGeneratorContext context, TypeModel implicitModel, string sourceVar, string stateVar, string indent = "            ", bool isMemberNullable = true)
    {
        bool isSimpleIdentifier = System.Text.RegularExpressions.Regex.IsMatch(sourceVar, "^[a-zA-Z0-9_]+$");
        
        string variableName = sourceVar;
        string wrapperPrefix = "";
        string wrapperSuffix = "";
        
        bool skipNullCheck = context.Model.TrustNullability && !isMemberNullable;

        if (!isSimpleIdentifier)
        {
            string safeBase = System.Text.RegularExpressions.Regex.Replace(sourceVar, "[^a-zA-Z0-9_]", "_");
            safeBase = System.Text.RegularExpressions.Regex.Replace(safeBase, "_+", "_").Trim('_');
            string safeName = $"l_{safeBase}_{context.GetNextVariableId()}";
            
            variableName = safeName;
            
            if (implicitModel.IsStruct)
            {
                wrapperPrefix = $"({sourceVar} is var {variableName} ? ";
                wrapperSuffix = $" : default({implicitModel.FullyQualifiedName}))";
            }
            else
            {
                if (skipNullCheck)
                {
                    wrapperPrefix = $"({sourceVar} is var {variableName} ? ";
                    wrapperSuffix = " : null!)";
                }
                else
                {
                    wrapperPrefix = $"({sourceVar} is var {variableName} && {variableName} != null ? ";
                    wrapperSuffix = " : null)";
                }
            }
        }
        else
        {
            if (!implicitModel.IsStruct && !skipNullCheck)
            {
                wrapperPrefix = $"{variableName} == null ? null : ";
                wrapperSuffix = "";
            }
        }

        string typeName = implicitModel.FullyQualifiedName;
        string innerIndent = indent + "    ";
        
        List<string> assignments = [];
        
        foreach (MemberModel member in implicitModel.Members)
        {
             string assign = GetMemberAssignment(context, member, variableName, stateVar, innerIndent);
             if (!string.IsNullOrEmpty(assign))
             {
                 assignments.Add(assign);
             }
        }

        string init = assignments.Count > 0 ? 
            $"new {typeName}\n{indent}{{\n{string.Join(",\n", assignments.Select(a => $"{indent}    {a}"))}\n{indent}}}" : 
            $"new {typeName}()";
        
        if (implicitModel.IsStruct && isSimpleIdentifier)
        {
            return init;
        }

        return wrapperPrefix + init + wrapperSuffix;
    }

    private static string GetHelperMethodCall(CloneGeneratorContext context, string methodName, string sourceExpression, bool needsState, string stateVar = "null")
    {
        string typeParams = GetTypeParametersString(context.Model);
        return needsState ? $"{methodName}{typeParams}({sourceExpression}, {stateVar})" : $"{methodName}{typeParams}({sourceExpression})";
    }

    private static string GetTypeParametersString(TypeModel model)
    {
        return model.TypeParameters.Count == 0 ? string.Empty : $"<{string.Join(", ", model.TypeParameters)}>";
    }
    
    private static string GetTypeNameFromFullName(string fullName)
    {
        int lastDot = fullName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < fullName.Length - 1)
        {
            return fullName.Substring(lastDot + 1);
        }
        return fullName;
    }

    /// <summary>
    /// Returns the precomputed extension class FQN for a clonable member type.
    /// </summary>
    private static string GetExtensionClassName(MemberModel member)
    {
        return member.ClonableExtensionClass!;
    }

    /// <summary>
    /// Returns the precomputed extension class FQN for a clonable collection element type.
    /// </summary>
    public static string GetExtensionClassNameForType(MemberModel member)
    {
        return member.ElementClonableExtensionClass!;
    }
}