using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义生成的序列化代码无法为成员赋值时报告的诊断。
/// </summary>
public static class InaccessibleSetterDiagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InaccessibleSetter;
    private const string Category = "Usage";
    private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.InaccessibleSetterTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.InaccessibleSetterMessageFormat), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.InaccessibleSetterDescription), Resources.ResourceManager, typeof(Resources));

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    /// <summary>
    /// 为不可访问的成员 setter 创建诊断。
    /// </summary>
    /// <param name="location">与诊断关联的源代码位置。</param>
    /// <param name="identifier">无法赋值的成员标识符。</param>
    /// <returns>诊断。</returns>
    public static Diagnostic CreateDiagnostic(Location? location, string identifier) => Diagnostic.Create(Rule, location, identifier);
}
