using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests
{
    /// <summary>
    /// 用于测试可防止在 grain 接口方法中参考、输出和参数的的分析器。
    /// Orleans grain 电话被发出RPC调频计呼,这些调频计按序列参数编号,因此可参考/发送/输入
    /// 无法像本地方法调用时那样工作参数。 此分析器帮助开发者
    /// 避免这一常见错误,并确保 grain 接口遵循 Orleans 方案拟订模式。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Analyzer")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Analyzer")]
    public class NoRefParamsDiagnosticAnalyzerTest : DiagnosticAnalyzerTestBase<NoRefParamsDiagnosticAnalyzer>
    {
        /// <summary>
        /// 证明分析器在 grain 接口方法中检测到参考参数。
        /// 参考参数不能在分布式电话中发挥作用,因为需要
        /// 共享存取, 在分布式系统中是不可能的 。
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
        /// 证明分析器在 grain 接口方法中检测出参数。
        /// 外线参数无法在分布式电话中发挥作用,因为需要
        /// 能够回写到呼叫者的记忆中,这是在RPC中是不可能的。
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
        /// 证明分析器检测到多反折/出参数违反
        /// 在 grain 接口方法中,确保所有问题参数都得到报告。
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
        /// 验证分析器允许在 grain 接口方法中使用常规参数
        /// 并且不干扰非grain 方法中的参考/输出/参数,
        /// 不属于 Orleans 限制范围。
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
        /// 证明静态接口方法可使用参考/输出参数。
        /// 静态方法不参与 grain 当地拨打并执行
        /// 所以他们不受分配的呼叫限制
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
