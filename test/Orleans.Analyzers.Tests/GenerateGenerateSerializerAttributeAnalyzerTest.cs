using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 用于建议使用[GenerateSerializer]而不是[可序列]属性的分析器测试。
/// Orleans 使用自己的序列系统进行高效 grain 通信和[GenerateSerializer
/// 属性使代码生成能够用于最佳序列化性能。使用遗留 [可存储]
/// 属性可能导致序列化速度放慢和信件大小较大。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class GenerateGenerateSerializerAttributeAnalyzerTest : DiagnosticAnalyzerTestBase<GenerateGenerateSerializerAttributeAnalyzer>
{
    private async Task VerifyGeneratedDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, new string[0]);

        Assert.NotEmpty(diagnostics);
        Assert.Single(diagnostics);

        var diagnostic = diagnostics.First();
        Assert.Equal(GenerateGenerateSerializerAttributeAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    private async Task VerifyNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, new string[0]);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 证明分析器检测到某一类使用 [可序列]属性时,分析器会检测
    /// 建议使用[GenerateSerializer而不是为了更好 Orleans 连续性业绩。
    /// </summary>
    [Fact]
    public Task SerializableClass()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public class D { }");

    /// <summary>
    /// 证明分析器在构造使用[可序列]属性时能够检测到
    /// 建议使用[GenerateSerializer而不是为了更好 Orleans 连续性业绩。
    /// </summary>
    [Fact]
    public Task SerializableStruct()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public struct D { }");

    /// <summary>
    /// 证明分析器在记录使用[可编 属性时检测到
    /// 建议使用[GenerateSerializer而不是为了更好 Orleans 连续性业绩。
    /// </summary>
    [Fact]
    public Task SerializableRecord()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public record D { }");

    /// <summary>
    /// 证明分析器在记录结构使用[可序列]属性时能够检测到
    /// 建议使用[GenerateSerializer而不是为了更好 Orleans 连续性业绩。
    /// </summary>
    [Fact]
    public Task SerializableRecordStruct()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public record struct D { }");

    [Fact]
    public Task SerializableClassWithGenerateSerializer_ShouldNotTriggerDiagnostic()
        => VerifyNoDiagnostic(
            """
            [System.Serializable, GenerateSerializer]
            public class D
            {
            }
            """);

    [Fact]
    public Task UserDefinedGenerateSerializerAttribute_ShouldTriggerDiagnostic()
        => VerifyGeneratedDiagnostic(
            """
            namespace Custom
            {
                public sealed class GenerateSerializerAttribute : System.Attribute
                {
                }
            }

            [System.Serializable, Custom.GenerateSerializer]
            public class D
            {
            }
            """);

    [Fact]
    public async Task PartialType_WithGenerateSerializerOnAnotherDeclaration_ShouldNotTriggerDiagnostic()
    {
        const string source = """
            using Serializer = Orleans.GenerateSerializerAttribute;

            [System.Serializable] public partial class D { }
            [Serializer] public partial class D { }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Empty(diagnostics);
    }
}
