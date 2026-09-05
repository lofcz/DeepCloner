using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace FastCloner.SourceGenerator;

internal static class BridgeContractCollector
{
    private const string BridgeTypeMetadataName = "FastCloner.FastClonerSourceGeneratorBridge";
    private const string BridgeAttrFqn = "FastCloner.FastClonerSourceGeneratorBridgeAttribute";
    private const string BridgeMemberAttrFqn = "FastCloner.FastClonerSourceGeneratorBridgeMemberAttribute";
    
    public static BridgeContract Collect(Compilation compilation)
    {
        INamedTypeSymbol? bridge = compilation.GetTypeByMetadataName(BridgeTypeMetadataName);

        AttributeData? bridgeAttr = bridge?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == BridgeAttrFqn);
        if (bridgeAttr == null || bridgeAttr.ConstructorArguments.Length == 0 || bridgeAttr.ConstructorArguments[0].Value is not string proxyFqn || string.IsNullOrEmpty(proxyFqn))
            return BridgeContract.Empty;

        List<BridgeMethodSpec> methods = [];
        
        foreach (ISymbol member in bridge?.GetMembers() ?? [])
        {
            if (member is not IMethodSymbol method)
                continue;
            if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic)
                continue;

            bool hasMemberAttr = method.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == BridgeMemberAttrFqn);
            if (!hasMemberAttr)
                continue;

            string returnTypeFqn = method.ReturnsVoid
                ? "void"
                : TypeAnalyzer.GetTypeNameForUsage(method.ReturnType);

            string[] paramFqns = new string[method.Parameters.Length];
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                paramFqns[i] = TypeAnalyzer.GetTypeNameForUsage(method.Parameters[i].Type);
            }

            methods.Add(new BridgeMethodSpec(
                Name: method.Name,
                ReturnTypeFqn: returnTypeFqn,
                ParameterTypeFqns: new EquatableArray<string>(paramFqns)));
        }

        if (methods.Count == 0)
            return BridgeContract.Empty;

        BridgeMethodSpec[] sortedMethods = methods.OrderBy(m => m.Name, System.StringComparer.Ordinal).ToArray();

        return new BridgeContract(
            BridgeTypeMetadataName: BridgeTypeMetadataName,
            ProxyTypeFullName: proxyFqn!,
            Methods: new EquatableArray<BridgeMethodSpec>(sortedMethods),
            IsAvailable: true);
    }
}
