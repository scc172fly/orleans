using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义 <c>GenerateCodeForDeclaringAssemblyAttribute</c> 引用的类型没有声明程序集时报告的诊断。
/// </summary>
public static class GenerateCodeForDeclaringAssemblyAttribute_NoDeclaringAssembly_Diagnostic
{
    /// <summary>
    /// 诊断标识符。
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.GenerateCodeForDeclaringAssemblyAttribute_NoDeclaringAssembly;

    /// <summary>
    /// 诊断标题。
    /// </summary>
    public const string Title = "Types passed to GenerateCodeForDeclaringAssemblyAttribute must have a declaring assembly";

    /// <summary>
    /// 诊断消息格式。
    /// </summary>
    public const string MessageFormat = "The type {0} provided as an argument to {1} does not have a declaring assembly";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(AttributeData attribute, ITypeSymbol type)
    {
        var location = attribute.ApplicationSyntaxReference is { } syntaxReference
            ? syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span)
            : Location.None;
        return Diagnostic.Create(Rule, location, type.ToDisplayString(), attribute.ToString());
    }
}
