using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试禁止在 Grain 接口中使用属性的分析器。
/// Orleans Grain 接口不能有属性，因为 Grain 调用基于 RPC，
/// 属性会使本地状态和远程调用之间产生歧义。
/// 所有 Grain 交互都必须通过显式方法调用进行，
/// 以明确分布式通信边界。
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
    /// 验证仅包含方法（无属性）的 Grain 接口能够通过验证。
    /// 这是 Orleans Grain 接口的正确使用模式。
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
    /// 验证分析器能否检测 IGrain 接口中的属性。
    /// 不允许使用属性，因为它们会掩盖 Grain 调用的分布式特性，
    /// 并可能混淆本地状态与远程状态。
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
    /// 验证分析器能否检测 IAddressable 接口中的属性。
    /// IAddressable 是 Grain 的基础接口；为了保持编程模型一致性，
    /// 它对于属性也有同样的限制。
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
    /// 验证分析器能否检测 IGrainObserver 接口中的属性。
    /// Grain 观察者用于回调及通知，并且与 Grain 一样，
    /// 必须使用显式方法而不是属性。
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
    /// 验证 Grain 接口允许使用静态属性。
    /// 静态成员不参与 Grain 调用，可用于
    /// 接口级常量或工具函数。
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
    /// 验证 IAddressable 接口允许使用静态属性。
    /// 静态成员不参与 Grain 调用，可用于
    /// 接口级常量或工具函数。
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
