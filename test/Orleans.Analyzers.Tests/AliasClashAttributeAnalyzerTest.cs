using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试 AliasClashAttributeAnalyzer ,确保别名属性没有冲突值。
/// Orleans 使用别名进行序列和RPC中的类别识别,因此重复的别名将导致 
/// 运行时冲突。 该分析器在编译时间捕捉这些问题 。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class AliasClashAttributeAnalyzerTest : DiagnosticAnalyzerTestBase<AliasClashAttributeAnalyzer>
{
    private async Task VerifyHasDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.NotEmpty(diagnostics);
        var diagnostic = diagnostics.First();

        Assert.Equal(AliasClashAttributeAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private async Task VerifyHasNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 证明分析器检测到两个昆虫具有相同的别名属性值。
    /// </summary>
    [Fact]
    public Task Enum_ShouldTriggerDiagnostic()
    {
        var code = """
            [Alias("Enum")]
            public enum EA { V }

            [Alias("Enum")]
            public enum EB { V }
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到两个记录具有相同的别名属性值。
    /// </summary>
    [Fact]
    public Task Record_ShouldTriggerDiagnostic()
    {
        var code = """
            [Alias("Record")]
            public record RA(string P);

            [Alias("Record")]
            public record RB(string P);
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到两个记录支架具有相同的别名属性值时。
    /// </summary>
    [Fact]
    public Task RecordStruct_ShouldTriggerDiagnostic()
    {
        var code = """
            [Alias("RecordStruct")]
            public record struct RSA(string P);

            [Alias("RecordStruct")]
            public record struct RSB(string P);
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到两个类别具有相同的别名属性值。
    /// </summary>
    [Fact]
    public Task Class_ShouldTriggerDiagnostic()
    {
        var code = """
            [Alias("Class")]
            public class CA
            {
                [Id(0)] public string P { get; set; }
            }

            [Alias("Class")]
            public class CB
            {
                [Id(0)] public string P { get; set; }
            }
            
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到两个支架的化名属性值相同时。
    /// </summary>
    [Fact]
    public Task Struct_ShouldTriggerDiagnostic()
    {
        var code = """
            [Alias("Struct")]
            public struct SA
            {
                public SA() { }

                [Id(0)] public string P { get; set; }
            }

            [Alias("Struct")]
            public struct SB
            {
                public SB() { }

                [Id(0)] public string P { get; set; }
            }
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到两个 grain 接口具有相同别名属性值时, 即为同一 。
    /// 测试各种 grain 接口类型(IGrain, IGrainWithIntegerKey等)。
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task GrainInterface_ShouldTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
            [Alias("Interface")]
            public interface IA : {{grainInterface}} {}

            [Alias("Interface")]
            public interface IB : {{grainInterface}} {}
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器不触发与同一别名的非grain 接口,
    /// 化名仅对 grain 接口 Orleans.
    /// </summary>
    [Fact]
    public Task NonGrainInterface_ShouldNotTriggerDiagnostic()
    {
        var code = $$"""
            [Alias("Interface")]
            public interface IA {}

            [Alias("Interface")]
            public interface IB {}
            """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器检测到同一 grain 接口中两种方法有相同的别名时。
    /// 方法别名必须是接口内唯一的, 以便进行适当的 RPC 发送 。
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task GrainInterfaceMethod_ShouldTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
            public interface I : {{grainInterface}}
            {
                [Alias("Void")] Task Void(int a);
                [Alias("Void")] Task Void(long a);
            }
            """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器允许在同一 grain 接口中不同方法的不同别名。
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task GrainInterfaceMethod_ShouldNotTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
            public interface I : {{grainInterface}}
            {
                [Alias("Void")] Task Void(int a);
                [Alias("Void1")] Task Void(long a);
            }
            """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器允许在不同 grain 接口中使用相同方法别名,
    /// 方法别名只需在每个界面中独有即可。
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task DifferentGrainInterfaceMethod_ShouldNotTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
            public interface I1 : {{grainInterface}}
            {
                [Alias("Void")] Task Void(string a);
            }
                    
            public interface I2
            {
                [Alias("Void")] Task Void(string a);
            }
            """;

        return VerifyHasNoDiagnostic(code);
    }
}
