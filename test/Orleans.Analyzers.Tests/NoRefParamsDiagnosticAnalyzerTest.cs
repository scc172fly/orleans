using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests
{
    /// <summary>
    /// 测试禁止在 Grain 接口方法中使用 ref、out 和 in 参数的分析器。
    /// Orleans Grain 调用是会序列化参数的分布式 RPC 调用，因此 ref/out/in
    /// 参数无法像本地方法调用那样工作。此分析器帮助开发者
    /// 避免此常见错误，并确保 Grain 接口遵循 Orleans 编程模型。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Analyzer")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Analyzer")]
    public class NoRefParamsDiagnosticAnalyzerTest : DiagnosticAnalyzerTestBase<NoRefParamsDiagnosticAnalyzer>
    {
        /// <summary>
        /// 验证分析器能否检测 Grain 接口方法中的 ref 参数。
        /// ref 参数无法跨分布式调用工作，因为它需要
        /// 共享内存访问，而分布式系统不具备此能力。
        /// </summary>
        [Fact]
        public async Task NoRefParamsAllowedInGrainInterfaceMethods()
        {
            var code = @"public interface I : IGrain
                        {
                            Task GetSomeOtherThing(int a, ref int i);
                        }";

            var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

            Assert.NotEmpty(diagnostics);
            Assert.Single(diagnostics);

            var diagnostic = diagnostics.First();
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.MessageFormat, diagnostic.GetMessage());
        }

        /// <summary>
        /// 验证分析器能否检测 Grain 接口方法中的 out 参数。
        /// out 参数无法跨分布式调用工作，因为它需要
        /// 回写调用方内存的能力，而 RPC 无法做到这一点。
        /// </summary>
        [Fact]
        public async Task NoOutParamsAllowedInGrainInterfaceMethods()
        {
            var code = @"public interface I : IGrain
                        {
                            Task GetSomeOtherThing(out int i);
                        }";

            var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

            Assert.NotEmpty(diagnostics);
            Assert.Single(diagnostics);

            var diagnostic = diagnostics.First();
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.MessageFormat, diagnostic.GetMessage());
        }

        /// <summary>
        /// 验证分析器能否检测 Grain 接口方法中的多个 ref/out 参数违规，
        /// 确保所有有问题的参数均被报告。
        /// </summary>
        [Fact]
        public async Task NoRefParamsDiagnosticAnalyzerInClass()
        {
            var code = @"public interface I : IGrain
                        {
                            Task GetSomething(ref int i);
                            Task GetSomeOtherThing(out int i);
                        }

                        public class Imp : I
                        {
                            public Task GetSomething(ref int i) => Task.CompletedTask;
                            public Task GetSomeOtherThing(out int i)
                            {
                                i = 0;
                                return Task.CompletedTask;
                            }
                        }
                        ";

            var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

            Assert.NotEmpty(diagnostics);
            Assert.Equal(2, diagnostics.Length);

            var diagnostic = diagnostics.First();
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(NoRefParamsDiagnosticAnalyzer.MessageFormat, diagnostic.GetMessage());
        }

        /// <summary>
        /// 验证分析器允许 Grain 接口方法使用普通参数，
        /// 且不会干预非 Grain 方法中的 ref/out/in 参数，
        /// 因为它们不在 Orleans 限制范围内。
        /// </summary>
        [Fact]
        public async Task NoRefParamsDiagnosticAnalyzerNoError()
        {
            var code = @"public interface I : IGrain
                        {
                            Task GetSomething(int i);
                        }

                        public class Imp : I
                        {
                            public Task GetSomething(int i) => Task.CompletedTask;
                            public Task SomeNonInterfaceMethod(ref int value, out int value2, in int value3)
                            {
                                value = 0;
                                value2 = 0;
                                return Task.CompletedTask;
                            }
                        }
                        ";

            var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

            Assert.Empty(diagnostics);
        }

        /// <summary>
        /// 验证静态接口方法可以使用 ref/out 参数。
        /// 静态方法不参与 Grain 调用，而是在本地执行，
        /// 因此不受分布式调用限制。
        /// </summary>
        [Fact]
        public async Task OutAndRefParamsAllowedInStaticGrainInterfaceMethods()
        {
            var code = @"public interface I : IGrain
                        {
                            public static bool SomeStaticMethod(out int o, ref int v) { o = 0; return false; }
                            public static virtual bool SomeStaticVirtualMethod(out int o, ref int v) { o = 0; return false; }
                        }";

            var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
            Assert.Empty(diagnostics);
        }
    }
}
