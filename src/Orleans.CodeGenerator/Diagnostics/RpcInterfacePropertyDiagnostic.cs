using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义 RPC 接口声明属性时报告的诊断。
/// </summary>
public static class RpcInterfacePropertyDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.RpcInterfaceProperty;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "RPC interfaces must not contain properties";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "The interface {0} contains a property {1}. RPC interfaces must not contain properties.";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static Diagnostic CreateDiagnostic(INamedTypeSymbol interfaceSymbol, IPropertySymbol property) => Diagnostic.Create(Rule, property.Locations.First(), interfaceSymbol.ToDisplayString(), property.ToDisplayString());
}
