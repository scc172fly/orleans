using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 当 RPC 方法有不支持的返回类型时,定义所报告诊断。
/// </summary>
public static class InvalidRpcMethodReturnTypeDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InvalidRpcMethodReturnType;
    private const string Category = "Usage";
    private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeMessageFormat), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.InvalidRpcMethodReturnTypeDescription), Resources.ResourceManager, typeof(Resources));

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    /// <summary>
    /// 创建 RPC 方法的诊断,该方法具有不支持的返回类型。
    /// </summary>
    /// <param name="location">与诊断有关的源位置。</param>
    /// <param name="returnType">不支持的返回类型。</param>
    /// <param name="methodIdentifier">辐射防护中心方法的识别符号。</param>
    /// <param name="supportedReturnTypeList">支持的返回类型显示列表。</param>
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
