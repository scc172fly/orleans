using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests
{
    /// <summary>
    /// 测试 AlwaysInterleaveDiagnosticAnalyzer，该分析器确保 [AlwaysInterleave] 特性
    /// 仅用于 Grain 接口的方法，而非 Grain 实现的方法。必须在接口上
    /// 声明此特性，以便为所有实现正确配置交错执行行为。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Analyzer")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Analyzer")]
    public class AlwaysInterleaveDiagnosticAnalyzerTest : DiagnosticAnalyzerTestBase<AlwaysInterleaveDiagnosticAnalyzer>
    {
        protected override Task<(Diagnostic[], string)> GetDiagnosticsAsync(string source, params string[] extraUsings)
            => base.GetDiagnosticsAsync(source, extraUsings.Concat(new[] { "Orleans.Concurrency" }).ToArray());

        /// <summary>
        /// 验证完全不使用 [AlwaysInterleave] 特性时不会报告诊断。
        /// </summary>
        [Fact]
        public async Task AlwaysInterleave_Analyzer_NoWarningsIfAttributeIsNotUsed() => await this.AssertNoDiagnostics(@"
class C
{
    Task M() => Task.CompletedTask;
}
");

        /// <summary>
        /// 验证在 Grain 接口方法上正确使用 [AlwaysInterleave] 特性时不会报告诊断。
        /// 这是该特性的正确使用方式。
        /// </summary>
        [Fact]
        public async Task AlwaysInterleave_Analyzer_NoWarningsIfAttributeIsUsedOnInterface() => await this.AssertNoDiagnostics(@"
public interface I : IGrain
{
    [AlwaysInterleave]
    Task<string> M();
}
");

        /// <summary>
        /// 验证错误使用 [AlwaysInterleave] 特性时会报告诊断错误，
        /// 即将其应用于 Grain 实现方法而不是接口方法时。这属于错误，因为交错执行
        /// 行为必须在接口层级指定。
        /// </summary>
        [Fact]
        public async Task AlwaysInterleave_Analyzer_WarningIfAttributeisUsedOnGrainClass()
        {
            var (diagnostics, source) = await this.GetDiagnosticsAsync(@"
public interface I : IGrain
{
    Task<int> Method();
}

public class C : I
{
    [AlwaysInterleave]
    public Task<int> Method() => Task.FromResult(0);
}
");

            var diagnostic = diagnostics.Single();

            Assert.Equal(AlwaysInterleaveDiagnosticAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(AlwaysInterleaveDiagnosticAnalyzer.MessageFormat, diagnostic.GetMessage());

            var span = diagnostic.Location.SourceSpan;
            Assert.Equal("AlwaysInterleave", source[span.Start..span.End]);
        }
    }
}
