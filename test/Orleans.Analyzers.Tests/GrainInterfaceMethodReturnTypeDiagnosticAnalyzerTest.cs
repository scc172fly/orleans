using System.Text;
using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试已注册的 Grain 调用返回类型，以及针对每种生成代理接收方的解析方式。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class GrainInterfaceMethodReturnTypeDiagnosticAnalyzerTest : DiagnosticAnalyzerTestBase<GrainInterfaceMethodReturnTypeDiagnosticAnalyzer>
{
    private const string DiagnosticId = GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.DiagnosticId;
    private const string MessageFormat = GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.MessageFormat;

    /// <summary>
    /// 验证分析器接受有效的 Grain 接口方法返回类型：
    /// Task、Task&lt;T&gt;、ValueTask、ValueTask&lt;T&gt; 和 void 均允许使用，因为它们
    /// 支持 Orleans 中 Grain 调用的异步特性。
    /// </summary>
    [Fact]
    public async Task GrainInterfaceMethodReturnTypeNoError()
    {
        var code = """
                    public interface IG : Orleans.IGrain
                    {
                        Task TaskMethod(int a);
                        Task<int> TaskOfIntMethod(int a);
                        ValueTask ValueTaskMethod(int a);
                        ValueTask<int> ValueTaskOfIntMethod(int a);
                        void VoidMethod(int a);
                    }

                    public interface IA : Orleans.Runtime.IAddressable
                    {
                        Task TaskMethod(int a);
                        Task<int> TaskOfIntMethod(int a);
                        ValueTask ValueTaskMethod(int a);
                        ValueTask<int> ValueTaskOfIntMethod(int a);
                        void VoidMethod(int a);
                    }

                    public interface IGO : Orleans.IGrainObserver
                    {
                        Task TaskMethod(int a);
                        Task<int> TaskOfIntMethod(int a);
                        ValueTask ValueTaskMethod(int a);
                        ValueTask<int> ValueTaskOfIntMethod(int a);
                        void VoidMethod(int a);
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 验证分析器能否检测返回同步类型（int）的 Grain 接口方法。
    /// 这是无效的，因为 Grain 调用必须是异步的，才能跨分布式集群工作。
    /// </summary>
    [Fact]
    public async Task IncompatibleGrainInterfaceMethodReturnType()
    {
        var code = """
                    public interface I : Orleans.IGrain
                    {
                        int MyMethod(int a);
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
    /// 验证静态接口方法可以使用任意返回类型，因为它们不受
    /// Grain 调用限制的约束。静态方法不参与
    /// 分布式 Grain 调用机制。
    /// </summary>
    [Fact]
    public async Task StaticInterfaceMethodsWithRegularReturnsAreAllowed()
    {
        var code = """
                    public interface I : Orleans.IGrain
                    {
                        public static int GetSomeOtherThing(int a) => 0;
                        public static virtual int GetSomeOtherThingVirtual(int a) => 0;
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RegisteredCustomReturnTypeIsAllowed()
    {
        var code = """
                    [Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall<>),
                        typeof(CustomRequest<>))]
                    public class CustomCall<T> { }

                    [Orleans.Invocation.ReturnValueProxy(nameof(InitializeRequest))]
                    public abstract class CustomRequest<T>
                    {
                        public CustomCall<T> InitializeRequest(Orleans.Runtime.GrainReference proxy) => new();
                    }

                    public interface I : Orleans.IGrain
                    {
                        CustomCall<int> MyMethod();
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AssemblyRegistrationCannotReplaceProxyDefault()
    {
        var code = """
                    [assembly: Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(Task),
                        typeof(CustomRequest))]

                    public abstract class CustomRequest { }

                    public interface I : Orleans.IGrain
                    {
                        Task MyMethod();
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.InvalidMappingDiagnosticId, diagnostic.Id);
        Assert.Contains("cannot replace proxy default", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InheritedMethodInitializerIsValidatedForDerivedProxyReceiver()
    {
        var code = """
                    [Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall),
                        typeof(CustomRequest))]
                    public class CustomCall { }

                    [Orleans.Invocation.ReturnValueProxy(nameof(InitializeRequest))]
                    public abstract class CustomRequest
                    {
                        public CustomCall InitializeRequest(IBase proxy) => new();
                        public object InitializeRequest(IDerived proxy) => new();
                    }

                    public interface IBase : Orleans.IGrain
                    {
                        CustomCall Call();
                    }

                    public interface IDerived : IBase { }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.InvalidMappingDiagnosticId, diagnostic.Id);
        Assert.Contains("Return-value proxy initializer", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ConstructedInheritedMethodInitializerIsValidatedForDerivedProxyReceiver()
    {
        var code = """
                    [Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall<>),
                        typeof(CustomRequest<>))]
                    public class CustomCall<T> { }

                    [Orleans.Invocation.ReturnValueProxy(nameof(InitializeRequest))]
                    public abstract class CustomRequest<T>
                    {
                        public CustomCall<T> InitializeRequest(IBase<T> proxy) => new();
                        public object InitializeRequest(IDerived proxy) => new();
                    }

                    public interface IBase<T> : Orleans.IGrain
                    {
                        CustomCall<T> Call();
                    }

                    public interface IDerived : IBase<int> { }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.InvalidMappingDiagnosticId, diagnostic.Id);
        Assert.Contains("Return-value proxy initializer", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("ProxyA", "ProxyB", false)]
    [InlineData("ProxyB", "ProxyA", true)]
    public async Task DirectSerializerAttributesMatchGeneratorProxySelection(
        string firstProxy,
        string secondProxy,
        bool expectDiagnostic)
    {
        var code = $$"""
                    [assembly: Orleans.InvokableBaseType(typeof(ProxyA), typeof(CustomCall), typeof(CustomRequest))]

                    public abstract class ProxyA { }
                    public abstract class ProxyB { }
                    public class CustomCall { }
                    public abstract class CustomRequest { }

                    [Orleans.GenerateMethodSerializers(typeof({{firstProxy}}))]
                    [Orleans.GenerateMethodSerializers(typeof({{secondProxy}}))]
                    public interface ICustomGrain : Orleans.IGrain
                    {
                        CustomCall Call();
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        Assert.Equal(expectDiagnostic, diagnostics.Any(static diagnostic =>
            diagnostic.Id == GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.DiagnosticId));
    }

    [Theory]
    [InlineData("IProxyA", "IProxyB", false)]
    [InlineData("IProxyB", "IProxyA", true)]
    public async Task InheritedSerializerAttributesMatchGeneratorProxySelection(
        string firstInterface,
        string secondInterface,
        bool expectDiagnostic)
    {
        var code = $$"""
                    [assembly: Orleans.InvokableBaseType(typeof(ProxyA), typeof(CustomCall), typeof(CustomRequest))]

                    public abstract class ProxyA { }
                    public abstract class ProxyB { }
                    public class CustomCall { }
                    public abstract class CustomRequest { }

                    [Orleans.GenerateMethodSerializers(typeof(ProxyA))]
                    public interface IProxyA : Orleans.IGrain { }

                    [Orleans.GenerateMethodSerializers(typeof(ProxyB))]
                    public interface IProxyB : Orleans.IGrain { }

                    public interface ICustomGrain : {{firstInterface}}, {{secondInterface}}
                    {
                        CustomCall Call();
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        Assert.Equal(expectDiagnostic, diagnostics.Any(static diagnostic =>
            diagnostic.Id == GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task RegisteredCustomReturnTypeMustSupportEveryProxyBase()
    {
        var code = """
                    [Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall<>),
                        typeof(CustomRequest<>))]
                    public class CustomCall<T> { }

                    public abstract class CustomRequest<T> { }
                    public abstract class CustomProxy { }

                    [Orleans.GenerateMethodSerializers(typeof(CustomProxy))]
                    public interface I : Orleans.IGrain
                    {
                        CustomCall<int> MyMethod();
                    }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, new string[0]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticId, diagnostic.Id);
        Assert.Equal(MessageFormat, diagnostic.GetMessage());
    }

    [Fact]
    public async Task ConstructedNonGrainBaseMethodsUseCustomMappings()
    {
        var code = """
                    [assembly: Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall<int>),
                        typeof(CustomRequest))]

                    public class CustomCall<T> { }
                    public abstract class CustomRequest { }

                    public interface IBase<T>
                    {
                        CustomCall<T> Call();
                        Task Call(int value);
                    }

                    public interface IUnrelated<T>
                    {
                        int Call();
                    }

                    public interface IGrain : IBase<int>, Orleans.IGrain { }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Task", "int", false)]
    [InlineData("int", "Task", true)]
    public async Task MultipleConstructedViewsPreserveFirstMatchingView(
        string firstReturnType,
        string secondReturnType,
        bool expectDiagnostic)
    {
        var code = $$"""
                    public interface IBase<T>
                    {
                        T Call();
                    }

                    public interface IGrain : IBase<{{firstReturnType}}>, IBase<{{secondReturnType}}>, Orleans.IGrain { }
                    """;

        var (diagnostics, source) = await this.GetDiagnosticsAsync(code, []);

        if (expectDiagnostic)
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(DiagnosticId, diagnostic.Id);
            Assert.Equal("T Call();", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Fact]
    public async Task ConstructedDiamondPreservesDerivedProxyReceiver()
    {
        var code = """
                    [assembly: Orleans.InvokableBaseType(
                        typeof(Orleans.Runtime.GrainReference),
                        typeof(CustomCall<int>),
                        typeof(CustomRequest))]

                    public class CustomCall<T> { }

                    [Orleans.Invocation.ReturnValueProxy(nameof(InitializeRequest))]
                    public abstract class CustomRequest
                    {
                        public CustomCall<int> InitializeRequest(IGrain proxy) => new();
                    }

                    public interface IBase<T>
                    {
                        CustomCall<T> Call();
                    }

                    public interface ILeft : IBase<int> { }
                    public interface IRight : IBase<int> { }
                    public interface IGrain : ILeft, IRight, Orleans.IGrain { }
                    """;

        var (diagnostics, _) = await this.GetDiagnosticsAsync(code, []);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("MissingProxy", "InvalidProxy", DiagnosticId)]
    [InlineData("InvalidProxy", "MissingProxy", GrainInterfaceMethodReturnTypeDiagnosticAnalyzer.InvalidMappingDiagnosticId)]
    public async Task RelevantProxyContextsPreserveFirstDiagnostic(
        string firstProxy,
        string secondProxy,
        string expectedDiagnosticId)
    {
        var code = $$"""
                    [assembly: Orleans.InvokableBaseType(typeof(InvalidProxy), typeof(CustomCall), typeof(InvalidRequest))]

                    public abstract class MissingProxy { }
                    public abstract class InvalidProxy { }
                    public sealed class InvalidRequest { }
                    public class CustomCall { }

                    public interface IBase
                    {
                        CustomCall Call();
                    }

                    public interface IUnrelated : Orleans.IGrain
                    {
                        Task Call();
                    }

                    [Orleans.GenerateMethodSerializers(typeof({{firstProxy}}))]
                    public interface IFirst : IBase, Orleans.IGrain { }

                    [Orleans.GenerateMethodSerializers(typeof({{secondProxy}}))]
                    public interface ISecond : IBase, Orleans.IGrain { }
                    """;

        var (diagnostics, source) = await this.GetDiagnosticsAsync(code, []);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedDiagnosticId, diagnostic.Id);
        var diagnosticSource = source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
        if (expectedDiagnosticId == DiagnosticId)
        {
            Assert.Equal("CustomCall Call();", diagnosticSource);
        }
        else
        {
            Assert.Contains("must be a non-sealed, non-static class", diagnostic.GetMessage());
            Assert.StartsWith("Orleans.InvokableBaseType(", diagnosticSource);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task UnrelatedProxyContextsPreserveDiagnosticsAtScale(int interfaceCount)
    {
        var code = new StringBuilder();
        for (var index = 0; index < interfaceCount; index++)
        {
            code.AppendLine($$"""
                public interface IGrain{{index}} : Orleans.IGrain
                {
                    Task Valid();
                    int Invalid();
                }

                public interface IUnrelated{{index}}
                {
                    int Unchecked();
                }
                """);
        }

        var (diagnostics, source) = await this.GetDiagnosticsAsync(code.ToString(), []);

        Assert.Equal(interfaceCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticId, diagnostic.Id);
            Assert.Equal("int Invalid();", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        });
    }
}
