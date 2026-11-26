using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 当 RPC 方法声明多个取消符号参数时,定义报告的诊断。
/// </summary>
public static class MultipleCancellationTokenParametersDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.MultipleCancellationTokenParameters;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "Grain method has multiple parameters of type CancellationToken";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "The type {0} contains method {1} which has multiple CancellationToken parameters. Only a single CancellationToken parameter is supported.";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(IMethodSymbol symbol) => Diagnostic.Create(Rule, symbol.Locations.First(), symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), symbol.Name);
}
