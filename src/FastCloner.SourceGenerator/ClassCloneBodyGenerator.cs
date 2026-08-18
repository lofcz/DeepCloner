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
        
        if (isRecord && !useState)
        {
            WriteRecordCloneBody(ctx, typeName, sourceVarName);
            return;
        }
        
        if (useState)
        {
            WriteInstanceCreation(ctx, sb, typeName, hasParameterlessConstructor, isRecord, sourceVarName);

            string stateVarForAdd = stateVarName ?? "state";
            string nullConditional = useNullConditional ? "?" : "";
            sb.AppendLine($"            {stateVarForAdd}{nullConditional}.AddKnownRef({sourceVarName}, result);");
            sb.AppendLine();

            // Records are created through their copy constructor (`source with { }`); classes
            // without a parameterless constructor are created via GetUninitializedObject, so no
            // constructor ran and weaver state is missing (issue #48).
            bool instanceCreatedWithoutConstructor = !isRecord && !hasParameterlessConstructor;
            foreach (MemberModel member in ctx.Model.Members)
            {
                MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, stateVarForAdd, instanceCreatedWithoutConstructor);
            }
            
            sb.AppendLine();
            sb.AppendLine("            return result;");
        }
        else
        {
            const string stateVar = "null";
            
            if (hasParameterlessConstructor)
            {
                sb.Append($"            var result = new {typeName}");
                
                List<string> initOnlyMembers = [];
                foreach (MemberModel member in ctx.Model.Members)
                {
                    bool participatesInInitializer =
                        (member is { IsProperty: true, IsInitOnly: true } || member.IsRequired);
                    if (!participatesInInitializer)
                        continue;

                    if (member.AccessorStrategy != NonPublicAccessorStrategy.None)
                        continue;

                    string assignment = MemberCloneGenerator.GetMemberAssignment(ctx, member, sourceVarName, stateVar, "                ");
                    if (!string.IsNullOrEmpty(assignment))
                    {
                        initOnlyMembers.Add($"                {assignment}");
                    }
                }

                if (initOnlyMembers.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("            {");
                    sb.AppendLine(string.Join(",\n", initOnlyMembers));
                    sb.AppendLine("            };");
                }
                else
                {
                    sb.AppendLine("();");
                }
                
                foreach (MemberModel member in ctx.Model.Members)
                {
                    bool participatedInInitializer =
                        (member is { IsProperty: true, IsInitOnly: true } || member.IsRequired)
                        && member.AccessorStrategy == NonPublicAccessorStrategy.None;
                    if (participatedInInitializer)
                        continue;
                    
                    MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, stateVar);
                }
            }
            else
            {
                WriteInstanceCreation(ctx, sb, typeName, hasParameterlessConstructor, isRecord, sourceVarName);

                // GetUninitializedObject: no constructor ran (issue #48 — populate without
                // invoking property setters and share weaver state).
                foreach (MemberModel member in ctx.Model.Members)
                {
                    MemberCloneGenerator.WriteMemberCloning(ctx, member, "result", sourceVarName, stateVar, instanceCreatedWithoutConstructor: true);
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("            return result;");
        }
    }
    
    private static void WriteInstanceCreation(CloneGeneratorContext ctx, StringBuilder sb, string typeName, bool hasParameterlessConstructor, bool isRecord, string sourceVarName = "source")
    {
        if (isRecord)
        {
            sb.AppendLine($"            var result = {sourceVarName} with {{ }};");
        }
        else if (hasParameterlessConstructor)
        {
            List<string> requiredMembers = [];
            foreach (MemberModel member in ctx.Model.Members)
            {
                if (member.IsRequired)
                {
                    requiredMembers.Add($"                {member.Name} = default!");
                }
            }

            if (requiredMembers.Count > 0)
            {
                sb.AppendLine($"            var result = new {typeName}");
                sb.AppendLine("            {");
                sb.AppendLine(string.Join(",\n", requiredMembers));
                sb.AppendLine("            };");
            }
            else
            {
                sb.AppendLine($"            var result = new {typeName}();");
            }
        }
        else
        {
            WriteGetUninitializedObject(sb, typeName);
        }
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
}

