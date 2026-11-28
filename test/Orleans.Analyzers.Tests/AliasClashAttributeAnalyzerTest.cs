using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试 AliasClashAttributeAnalyzer，该分析器确保别名特性的值不会冲突。
/// Orleans 在序列化和 RPC 中使用别名标识类型，因此重复的别名会造成
/// 运行时冲突。此分析器在编译时捕获这些问题。
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
    /// 验证分析器能否检测具有相同别名特性值的两个枚举。
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
    /// 验证分析器能否检测具有相同别名特性值的两个记录类型。
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
    /// 验证分析器能否检测具有相同别名特性值的两个记录结构体。
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
    /// 验证分析器能否检测具有相同别名特性值的两个类。
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
    /// 验证分析器能否检测具有相同别名特性值的两个结构体。
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
    /// 验证分析器能否检测具有相同别名特性值的两个 Grain 接口。
    /// 测试多种 Grain 接口类型（IGrain、IGrainWithIntegerKey 等）。
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
    /// 验证对于具有相同别名的非 Grain 接口，分析器不会触发诊断，
    /// 因为在 Orleans 中，接口别名仅对 Grain 接口有意义。
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
    /// 验证分析器能否检测同一 Grain 接口中具有相同别名的两个方法。
    /// 为了正确分派 RPC，方法别名在同一接口内必须唯一。
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
    /// 验证分析器是否允许同一 Grain 接口中不同的方法使用不同别名。
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
    /// 验证分析器是否允许不同的 Grain 接口使用相同的方法别名，
    /// 因为方法别名只需要在各自接口内唯一。
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
