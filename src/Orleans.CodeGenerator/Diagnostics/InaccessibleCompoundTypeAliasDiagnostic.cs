using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 当复合型别名指生成代码无法进入的类型时,定义报告的诊断。
/// </summary>
public static class InaccessibleCompoundTypeAliasDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InaccessibleCompoundTypeAlias;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "Compound type alias must refer to accessible types";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "The compound type alias on {0} cannot be registered because the type {1} is inaccessible from generated code";

    /// <summary>
    /// 诊断描述
    /// </summary>
    public const string Description = "Compound type aliases are registered by generated code, which must be able to refer to the aliased type and to every type used in the alias. Either make those types public or make them internal and ensure that internals are visible to the generated code, or remove the alias.";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    internal static Diagnostic CreateDiagnostic(INamedTypeSymbol target, AttributeData attribute, ITypeSymbol inaccessibleType, Compilation compilation)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? target.Locations.FirstOrDefault()
            ?? Location.None;

        // 参考汇编保留了消费者诊断结果无法接受的源树。
        if (location.SourceTree is { } tree && !compilation.ContainsSyntaxTree(tree))
        {
            var lineSpan = location.GetLineSpan();
            location = Location.Create(lineSpan.Path, location.SourceSpan, lineSpan.Span);
        }

        return Diagnostic.Create(
            Rule,
            location,
            target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            inaccessibleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}
