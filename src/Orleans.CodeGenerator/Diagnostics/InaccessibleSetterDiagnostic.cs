using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义当生成序列代码无法指派成员时报告的诊断。
/// </summary>
public static class InaccessibleSetterDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string RuleId = DiagnosticRuleId.InaccessibleSetter;
    private const string Category = "Usage";
    private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.InaccessibleSetterTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.InaccessibleSetterMessageFormat), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.InaccessibleSetterDescription), Resources.ResourceManager, typeof(Resources));

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    /// <summary>
    /// 为无法进入的会籍设置器创建诊断。
    /// </summary>
    /// <param name="location">与诊断有关的源位置。</param>
    /// <param name="identifier">无法指定成员的身份识别符。</param>
    /// <returns>诊断。</returns>
    public static Diagnostic CreateDiagnostic(Location? location, string identifier) => Diagnostic.Create(Rule, location, identifier);
}
