using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义生成代码无法访问某个可序列化类型时报告的诊断。
/// </summary>
public static class InaccessibleSerializableTypeDiagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InaccessibleSerializableType;

    /// <summary>
    /// 诊断标题。
    /// </summary>
    public const string Title = "Serializable type must be accessible from generated code";

    /// <summary>
    /// 诊断消息格式。
    /// </summary>
    public const string MessageFormat = "The type {0} is marked as being serializable but it is inaccessible from generated code";

    /// <summary>
    /// 诊断描述。
    /// </summary>
    public const string Description = "Source generation requires that all types marked as serializable are accessible from generated code. Either make the type public or make it internal and ensure that internals are visible to the generated code.";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    internal static Diagnostic CreateDiagnostic(ISymbol symbol, Location? location = null) => Diagnostic.Create(Rule, location ?? symbol.Locations.First(), symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}
