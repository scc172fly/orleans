using Microsoft.CodeAnalysis;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试建议使用 [GenerateSerializer] 特性代替 [Serializable] 特性的分析器。
/// Orleans 使用自己的序列化系统实现高效的 Grain 通信，[GenerateSerializer]
/// 特性能够启用代码生成以优化序列化性能。使用旧版 [Serializable]
/// 特性可能导致序列化速度降低及消息体积增大。
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
    /// 验证分析器能否检测使用 [Serializable] 特性的类，
    /// 并建议改用 [GenerateSerializer] 以获得更好的 Orleans 序列化性能。
    /// </summary>
    [Fact]
    public Task SerializableClass()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public class D { }");

    /// <summary>
    /// 验证分析器能否检测使用 [Serializable] 特性的结构体，
    /// 并建议改用 [GenerateSerializer] 以获得更好的 Orleans 序列化性能。
    /// </summary>
    [Fact]
    public Task SerializableStruct()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public struct D { }");

    /// <summary>
    /// 验证分析器能否检测使用 [Serializable] 特性的记录类型，
    /// 并建议改用 [GenerateSerializer] 以获得更好的 Orleans 序列化性能。
    /// </summary>
    [Fact]
    public Task SerializableRecord()
        => VerifyGeneratedDiagnostic(@"[System.Serializable] public record D { }");

    /// <summary>
    /// 验证分析器能否检测使用 [Serializable] 特性的记录结构体，
    /// 并建议改用 [GenerateSerializer] 以获得更好的 Orleans 序列化性能。
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
