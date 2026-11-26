using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 当无法生成可连号类型的隐含字段识别符号时,定义报告的诊断。
/// </summary>
public static class CanNotGenerateImplicitFieldIdsDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.CanNotGenerateImplicitFieldIds;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "Implicit field identifiers could not be generated";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "Could not generate implicit field identifiers for the type {0}: {1}";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(ISymbol symbol, string reason, Location? location = null) => Diagnostic.Create(Rule, location ?? symbol.Locations.First(), symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), reason);
}
