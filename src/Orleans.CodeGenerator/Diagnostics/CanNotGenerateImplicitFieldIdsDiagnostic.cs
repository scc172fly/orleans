using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义无法为可序列化类型生成隐式字段标识符时报告的诊断。
/// </summary>
public static class CanNotGenerateImplicitFieldIdsDiagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.CanNotGenerateImplicitFieldIds;

    /// <summary>
    /// 诊断标题。
    /// </summary>
    public const string Title = "Implicit field identifiers could not be generated";

    /// <summary>
    /// 诊断消息格式。
    /// </summary>
    public const string MessageFormat = "Could not generate implicit field identifiers for the type {0}: {1}";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(ISymbol symbol, string reason, Location? location = null) => Diagnostic.Create(Rule, location ?? symbol.Locations.First(), symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), reason);
}
