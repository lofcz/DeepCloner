using System.Collections.Generic;
using System.Text;

namespace FastCloner.SourceGenerator;

internal static class ClassCloneBodyGenerator
{
    public static bool NeedsFormatterServices(TypeModel model)
    {
        return model is { HasParameterlessConstructor: false, IsStruct: false, IsRecord: false };
    }
    
    public static bool NeedsFormatterServices(IEnumerable<TypeModel> types)
    {
        foreach (TypeModel? type in types)
        {
            if (NeedsFormatterServices(type))
            {
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// Members that the C# compiler only allows to be assigned during construction
    /// (public <c>init</c> accessors and <c>required</c> members). Post-construction
    /// <c>result.X = ...</c> is a compile error for these, so every <c>new T()</c>
    /// path must put them in the object initializer. The state-tracking path used
    /// to skip them (ZeroCoolDade/FastClonerBug: init-only properties cloned as
    /// collection elements via InternalFastDeepClone).
    /// </summary>
    public static bool MustAssignInObjectInitializer(MemberModel member)
    {
        if (member.IsWeaverState)
            return false;
        if (member.AccessorStrategy != NonPublicAccessorStrategy.None)
            return false;
        return member is { IsProperty: true, IsInitOnly: true } || member.IsRequired;
    }

    public static List<string> CollectObjectInitializerAssignments(
        CloneGeneratorContext ctx,
        IEnumerable<MemberModel> members,
        string sourceVar,
        string stateVar)
    {
        List<string> assignments = [];
        foreach (MemberModel member in members)
        {
            if (!MustAssignInObjectInitializer(member))
                continue;

            string assignment = MemberCloneGenerator.GetMemberAssignment(ctx, member, sourceVar, stateVar, "                ");
            if (!string.IsNullOrEmpty(assignment))
                assignments.Add($"                {assignment}");
        }

        return assignments;
    }

    public static void WriteNewWithObjectInitializer(StringBuilder sb, string typeName, List<string> initializerAssignments)
    {
        if (initializerAssignments.Count > 0)
        {
            sb.AppendLine($"            var result = new {typeName}");
            sb.AppendLine("            {");
            sb.AppendLine(string.Join(",\n", initializerAssignments));
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine($"            var result = new {typeName}();");
        }
    }

    public static void WriteClassCloneBody(
        CloneGeneratorContext ctx,
        string typeName,
        bool useState,
        string? stateVarName = null,
        bool useNullConditional = false,
        string sourceVarName = "source")
    {
        StringBuilder sb = ctx.Source;
        bool hasParameterlessConstructor = ctx.Model.HasParameterlessConstructor;
        bool isRecord = ctx.Model.IsRecord;
        string stateVar = useState ? (stateVarName ?? "state") : "null";
        
        if (isRecord && !useState)
        {
            WriteRecordCloneBody(ctx, typeName, sourceVarName);
            return;
        }

        if (isRecord)
        {
            WriteRecordCloneBodyWithState(ctx, sourceVarName, stateVar, useNullConditional);
            return;
        }

        bool instanceCreatedWithoutConstructor = !hasParameterlessConstructor;

        if (hasParameterlessConstructor)
        {
            // Init/required members cannot be assigned after construction, regardless of
            // whether this body also tracks circular references.
            WriteNewWithObjectInitializer(sb, typeName, CollectObjectInitializerAssignments(ctx, ctx.Model.Members, sourceVarName, stateVar));
        }
        else
        {
            WriteGetUninitializedObject(sb, typeName);
        }

        if (useState)
        {
            string nullConditional = useNullConditional ? "?" : "";
            sb.AppendLine($"            {stateVar}{nullConditional}.AddKnownRef({sourceVarName}, result);");
            sb.AppendLine();
        }

        foreach (MemberModel member in ctx.Model.Members)
        {
            if (hasParameterlessConstructor && MustAssignInObjectInitializer(member))
                continue;

            MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, stateVar, instanceCreatedWithoutConstructor);
        }
            
        sb.AppendLine();
        sb.AppendLine("            return result;");
    }
    
    internal static void WriteGetUninitializedObject(StringBuilder sb, string typeName)
    {
        sb.AppendLine("#if NET5_0_OR_GREATER");
        sb.AppendLine($"            var result = ({typeName})System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({typeName}));");
        sb.AppendLine("#else");
        sb.AppendLine($"            var result = ({typeName})System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof({typeName}));");
        sb.AppendLine("#endif");
    }
    
    private static void WriteRecordCloneBody(CloneGeneratorContext ctx, string typeName, string sourceVarName = "source")
    {
        StringBuilder sb = ctx.Source;
        List<string> deepCloneAssignments = [];
        List<MemberModel> getterOnlyCollections = [];
        
        foreach (MemberModel member in ctx.Model.Members)
        {
            if (member is { IsProperty: true, HasGetter: true, HasSetter: false, IsInitOnly: false })
            {
                if (member.TypeKind == MemberTypeKind.Collection || member.TypeKind == MemberTypeKind.Dictionary)
                {
                    getterOnlyCollections.Add(member);
                }
                continue;
            }
            
            if (member.TypeKind == MemberTypeKind.Safe)
                continue;

            if (member.IsReadOnly)
                continue;
            
            string assignment = MemberCloneGenerator.GetMemberAssignment(ctx, member, sourceVarName, "null", "                ");
            if (!string.IsNullOrEmpty(assignment))
            {
                deepCloneAssignments.Add($"                {assignment}");
            }
        }
        
        if (getterOnlyCollections.Count > 0)
        {
            if (deepCloneAssignments.Count == 0)
            {
                sb.AppendLine($"            var result = {sourceVarName} with {{ }};");
            }
            else
            {
                sb.AppendLine($"            var result = {sourceVarName} with");
                sb.AppendLine("            {");
                sb.AppendLine(string.Join(",\n", deepCloneAssignments));
                sb.AppendLine("            };");
            }
            
            foreach (MemberModel member in getterOnlyCollections)
            {
                MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, "null");
            }
            
            sb.AppendLine();
            sb.AppendLine("            return result;");
        }
        else if (deepCloneAssignments.Count == 0)
        {
            sb.AppendLine($"            return {sourceVarName} with {{ }};");
        }
        else
        {
            sb.AppendLine($"            return {sourceVarName} with");
            sb.AppendLine("            {");
            sb.AppendLine(string.Join(",\n", deepCloneAssignments));
            sb.AppendLine("            };");
        }
    }

    /// <summary>
    /// State-tracking record clone: construction-only members (init/required) go in
    /// <c>with {{ }}</c> because they cannot be assigned afterwards; settable members
    /// are written after the instance is registered so cycles through them resolve.
    /// </summary>
    private static void WriteRecordCloneBodyWithState(
        CloneGeneratorContext ctx,
        string sourceVarName,
        string stateVar,
        bool useNullConditional)
    {
        StringBuilder sb = ctx.Source;
        List<string> constructionAssignments = CollectObjectInitializerAssignments(ctx, ctx.Model.Members, sourceVarName, stateVar);

        if (constructionAssignments.Count > 0)
        {
            sb.AppendLine($"            var result = {sourceVarName} with");
            sb.AppendLine("            {");
            sb.AppendLine(string.Join(",\n", constructionAssignments));
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine($"            var result = {sourceVarName} with {{ }};");
        }

        string nullConditional = useNullConditional ? "?" : "";
        sb.AppendLine($"            {stateVar}{nullConditional}.AddKnownRef({sourceVarName}, result);");
        sb.AppendLine();

        foreach (MemberModel member in ctx.Model.Members)
        {
            if (MustAssignInObjectInitializer(member))
                continue;

            MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, stateVar);
        }

        sb.AppendLine();
        sb.AppendLine("            return result;");
    }
}

