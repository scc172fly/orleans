using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 用于测试 grain 接口中防止属性的的分析器。
/// Orleans grain 接口不能有属性,因为 grain 调用是以 RPC为基础的
/// 以及属性会让本地州电话和远程通话之间产生模糊性。
/// 所有 grain 互动必须明确方法,要求保持清晰
/// 有关分布式通信边界。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class GrainInterfacePropertyDiagnosticAnalyzerTest : DiagnosticAnalyzerTestBase<GrainInterfacePropertyDiagnosticAnalyzer>
{
    private const string DiagnosticId = GrainInterfacePropertyDiagnosticAnalyzer.DiagnosticId;
    private const string MessageFormat = GrainInterfacePropertyDiagnosticAnalyzer.MessageFormat;

    /// <summary>
    /// 证明 grain 接口仅具有方法(无属性)传承验证。
    /// 这是 Orleans grain 接口的正确模式。
    /// </summary>
    [Fact]
    public async Task GrainInterfacePropertyNoError()
    {
        var code = """
                    public interface I : Orleans.IGrain
                    {
                        Task GetSomeOtherThing(int a);
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 证明分析器检测到IGrain界面的属性。
    /// 不允许属性, 因为它们会模糊分布性质 。
    /// 电话的 grain 通话,可能导致对本地与远程状态的混淆。
    /// </summary>
    [Fact]
    public async Task NoPropertiesAllowedInGrainInterface()
    {
        var code = """
                    public interface I : Orleans.IGrain
                    {
                        int MyProperty { get; set; }
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

        Assert.NotEmpty(diagnostics);
        Assert.Single(diagnostics);

        var diagnostic = diagnostics.First();
        Assert.Equal(DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MessageFormat, diagnostic.GetMessage());
    }

    /// <summary>
    /// 证明分析器检测到可处理界面中的属性。
    /// grains 的基础接口是可加处理的,并且有相同的限制
    /// 保持方案拟订模式的一致性。
    /// </summary>
    [Fact]
    public async Task NoPropertiesAllowedInIAddressableInterface()
    {
        var code = """
                    public interface I : Orleans.Runtime.IAddressable
                    {
                        int MyProperty { get; set; }
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

        Assert.NotEmpty(diagnostics);
        Assert.Single(diagnostics);

        var diagnostic = diagnostics.First();
        Assert.Equal(DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MessageFormat, diagnostic.GetMessage());
    }

    /// <summary>
    /// 证明分析器在 IGrainObserver 接口中检测到属性。
    /// Grain 观察员用于回调和通知,以及类似 grains,
    /// 它们必须使用明确的方法,而不是属性。
    /// </summary>
    [Fact]
    public async Task NoPropertiesAllowedInIGrainObserverInterface()
    {
        var code = """
                    public interface I : Orleans.IGrainObserver
                    {
                        int MyProperty { get; set; }
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

        Assert.NotEmpty(diagnostics);
        Assert.Single(diagnostics);

        var diagnostic = diagnostics.First();
        Assert.Equal(DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MessageFormat, diagnostic.GetMessage());
    }

    /// <summary>
    /// 证明允许在 grain 接口中输入静态属性。
    /// 静态成员不参加 grain 祈祷,可以使用
    /// 用于接口级常数或通用功能。
    /// </summary>
    [Fact]
    public async Task StaticInterfacePropertiesAllowedInGrainInterface()
    {
        var code = """
                    public interface I : Orleans.IGrain
                    {
                        public static int MyProperty => 0;
                        public static virtual int MyVirtualProperty => 0;
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 证明可安装界面时允许静态属性。
    /// 静态成员不参加 grain 祈祷,可以使用
    /// 用于接口级常数或通用功能。
    /// </summary>
    [Fact]
    public async Task StaticInterfacePropertiesAllowedInAddressableInterface()
    {
        var code = """
                    public interface I : Orleans.Runtime.IAddressable
                    {
                        public static int MyProperty => 0;
                        public static virtual int MyVirtualProperty => 0;
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonInterfaceProperties_IncludingGrainImplementations_ShouldNotTriggerDiagnostic()
    {
        const string source = """
            using Orleans;

            public interface ITestGrain : IGrainWithIntegerKey { }
            public class C : ITestGrain { public int Value { get; set; } }
            public struct S : ITestGrain { public int Value { get; set; } }
            public record R : ITestGrain { public int Value { get; set; } }
            public record struct RS : ITestGrain { public int Value { get; set; } }
            public class Implementation : Grain, ITestGrain
            {
                public int Value { get; set; }
            }

            public interface IContainer : ITestGrain
            {
                public class Nested
                {
                    public int Value { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NestedPartialGrainInterfaces_ShouldReportExactPropertySpans()
    {
        const string firstProperty = "int First { get; set; }";
        const string secondProperty = "int Second { get; }";
        var source = $$"""
            using GrainBase = Orleans.IGrainWithIntegerKey;

            public class Container
            {
                public interface IBase : GrainBase { }

                public partial interface IChild
                {
                    {{firstProperty}}
                }

                public partial interface IChild : IBase
                {
                    {{secondProperty}}
                }

                public interface ILocal
                {
                    int Local { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Collection(diagnostics,
            diagnostic => AssertPropertyDiagnostic(diagnostic, source, firstProperty),
            diagnostic => AssertPropertyDiagnostic(diagnostic, source, secondProperty));
    }

    [Fact]
    public async Task GeneratedGrainInterface_ShouldReportExactPropertySpan()
    {
        const string property = "int Value { get; set; }";
        var source = $$"""
            // <auto-generated/>
            public interface IGenerated : Orleans.IGrainWithGuidKey
            {
                {{property}}
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        AssertPropertyDiagnostic(Assert.Single(diagnostics), source, property);
    }

    private static void AssertPropertyDiagnostic(Diagnostic diagnostic, string source, string property)
    {
        Assert.Equal(DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MessageFormat, diagnostic.GetMessage());
        Assert.Equal(new TextSpan(source.IndexOf(property, StringComparison.Ordinal), property.Length), diagnostic.Location.SourceSpan);
    }
}
