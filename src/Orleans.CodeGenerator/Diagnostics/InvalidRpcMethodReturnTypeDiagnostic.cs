using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义 RPC 方法的返回类型不受支持时报告的诊断。
/// </summary>
public static class InvalidRpcMethodReturnTypeDiagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InvalidRpcMethodReturnType;
    private const string Category = "Usage";
    private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeMessageFormat), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeDescription), Resources.ResourceManager, typeof(Resources));

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    /// <summary>
    /// 为返回类型不受支持的 RPC 方法创建诊断。
    /// </summary>
    /// <param name="location">与诊断关联的源代码位置。</param>
    /// <param name="returnType">不受支持的返回类型。</param>
    /// <param name="methodIdentifier">RPC 方法的标识符。</param>
    /// <param name="supportedReturnTypeList">用于显示的受支持返回类型列表。</param>
    /// <returns>诊断。</returns>
    public static Diagnostic CreateDiagnostic(Location? location, string returnType, string methodIdentifier, string supportedReturnTypeList) => Diagnostic.Create(Rule, location, returnType, methodIdentifier, supportedReturnTypeList);

    internal static Diagnostic CreateDiagnostic(InvokableMethodDescription methodDescription)
    {
        var methodReturnType = methodDescription.Method.ReturnType;
        var diagnostic = CreateDiagnostic(
            methodDescription.Method.OriginalDefinition.Locations.FirstOrDefault(),
            methodReturnType.ToDisplayString(),
            methodDescription.Method.ToDisplayString(),
            string.Join(", ", methodDescription.InvokableBaseTypes.Keys.Select(v => v.ToDisplayString())));
        return diagnostic;
    }
}
