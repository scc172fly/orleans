using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 表示应作为编译器诊断报告的代码生成分析失败。
/// </summary>
/// <param name="diagnostic">描述失败的诊断。</param>
public class OrleansGeneratorDiagnosticAnalysisException(Diagnostic diagnostic) : Exception(diagnostic.GetMessage())
{
    /// <summary>
    /// 获取描述失败的诊断。
    /// </summary>
    public Diagnostic Diagnostic { get; } = diagnostic;
}
