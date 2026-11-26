using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 代码生成分析失败,应作为汇编者诊断报告。
/// </summary>
/// <param name="diagnostic">描述失败的诊断。</param>
public class OrleansGeneratorDiagnosticAnalysisException(Diagnostic diagnostic) : Exception(diagnostic.GetMessage())
{
    /// <summary>
    /// 获得描述失败的诊断。
    /// </summary>
    public Diagnostic Diagnostic { get; } = diagnostic;
}
