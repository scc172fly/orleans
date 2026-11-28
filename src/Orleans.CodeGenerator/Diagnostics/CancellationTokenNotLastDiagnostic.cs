using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义 RPC 方法的取消令牌参数未位于最后时报告的诊断。
/// </summary>
public static class CancellationTokenNotLastDiagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.CancellationTokenNotLast;

    /// <summary>
    /// 诊断标题。
    /// </summary>
    public const string Title = "CancellationToken should be the last RPC parameter";

    /// <summary>
    /// 诊断消息格式。
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
