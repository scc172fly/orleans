using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Diagnostics;

/// <summary>
/// 定义在 <c>GenerateSerializerAttribute</c> 应用于参考组装中的类型时报告的诊断。
/// </summary>
public static class ReferenceAssemblyWithGenerateSerializerDiagnostic
{
    /// <summary>
    /// 诊断识别器
    /// </summary>
    public const string DiagnosticId = DiagnosticRuleId.ReferenceAssemblyWithGenerateSerializer;

    /// <summary>
    /// 诊断标题
    /// </summary>
    public const string Title = "[GenerateSerializer] used in a reference assembly";

    /// <summary>
    /// 诊断信息格式
    /// </summary>
    public const string MessageFormat = "The type {0} is marked with [GenerateSerializer] in a reference assembly";

    /// <summary>
    /// 诊断描述
    /// </summary>
    public const string Description = "The type {0} is marked with [GenerateSerializer] in a reference assembly. Serialization is likely to fail. Options: (1) Enable code generation on the target project directly; (2) Disable reference assemblies using <ProduceReferenceAssembly>false</ProduceReferenceAssembly> in the codegen project; (3) Use a different serializer or create surrogates. See https://aka.ms/orleans-serialization for details.";

    /// <summary>
    /// 诊断类别。
    /// </summary>
    public const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

    internal static Diagnostic CreateDiagnostic(ISymbol symbol, Location? location = null) => Diagnostic.Create(Rule, location ?? symbol.Locations.First(), symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}
