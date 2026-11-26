using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests
{
    /// <summary>
    /// AlwaysInterleaveDiagnosticAnalyzer 的测试,确保[AlwaysInterleave]属性
    /// 仅用于 grain 接口方法,而不是 grain 执行方法。属性必须是
    /// 在所有执行中正确配置连接行为。
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
        /// 证明在[AlwaysInterleave]属性完全未使用时,没有报告诊断。
        /// </summary>
        [Fact]
        public async Task AlwaysInterleave_Analyzer_NoWarningsIfAttributeIsNotUsed() => await this.AssertNoDiagnostics(@"
class C
{
    Task M() => Task.CompletedTask;
}
");

        /// <summary>
        /// 证明当[AlwaysInterleave]属性正确用于 grain 接口方法时,没有报告诊断结果。
        /// 这是属性的正确使用模式 。
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
        /// 证明在错误使用 [AlwaysInterleave] 属性时报告诊断错误
        /// 而不是接口方法,在 grain 执行方法上实施。这是一个错误,因为中断
        /// 必须在界面级别指定行为。
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
