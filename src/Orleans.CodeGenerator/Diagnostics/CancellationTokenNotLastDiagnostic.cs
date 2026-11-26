using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 当 RPC 方法有非持续取消的代号参数时,定义所报告诊断。
/// </summary>
public static class CancellationTokenNotLastDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.CancellationTokenNotLast;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "CancellationToken should be the last RPC parameter";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "CancellationToken parameter '{0}' on RPC method '{1}' should be the last parameter";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(IParameterSymbol parameter) =>
        Diagnostic.Create(
            Rule,
            parameter.Locations.FirstOrDefault() ?? Location.None,
            parameter.Name,
            parameter.ContainingSymbol.ToDisplayString());
}
