using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试检测可序列化类型中重复 [Id] 特性的分析器。
/// 在 Orleans 序列化中，每个字段都必须具有唯一的 [Id] 特性值，以确保
/// 正确反序列化及版本容错。重复 ID 会导致 Grain 之间交换消息时的
/// 序列化冲突与数据损坏。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class IdClashAttributeAnalyzerTest : DiagnosticAnalyzerTestBase<IdClashAttributeAnalyzer>
{
    private async Task VerifyHasDiagnostic(string code, int diagnosticsCount)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.NotEmpty(diagnostics);
        Assert.Equal(diagnosticsCount, diagnostics.Length);

        var diagnostic = diagnostics.First();

        Assert.Equal(IdClashAttributeAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private async Task VerifyHasNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// 验证分析器能否检测带 [GenerateSerializer] 的类型中多个字段
    /// 具有相同 [Id] 值的情况。这会导致序列化冲突，因为序列化器在反序列化期间
    /// 无法确定 ID 对应哪个字段。
    /// </summary>
    [Fact]
    public Task TypesWithGenerateSerializerAndDuplicatedIds_ShouldTriggerDiagnostic()
    {
        var code = """
                    [GenerateSerializer]
                    public class C
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(1)] public string P3 { get; set; }
                    }

                    [GenerateSerializer]
                    public struct S
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(1)] public string P3 { get; set; }
                    }

                    [GenerateSerializer]
                    public record R(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(0)] public string P5 { get; set; }
                    }

                    [GenerateSerializer]
                    public record struct RS(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(0)] public string P5 { get; set; }
                    }
                    """;

        return VerifyHasDiagnostic(code, 8);
    }

    /// <summary>
    /// 验证带 [GenerateSerializer] 的类型中所有字段均使用唯一 [Id] 值时，
    /// 分析器不会报告问题。这是 Orleans 序列化的正确用法。
    /// </summary>
    [Fact]
    public Task TypesWithGenerateSerializerAndNoDuplicatedIds_ShouldNoTriggerDiagnostic()
    {
        var code = """
                    [GenerateSerializer]
                    public class C
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(2)] public string P3 { get; set; }
                    }

                    [GenerateSerializer]
                    public struct S
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(2)] public string P3 { get; set; }
                    }

                    [GenerateSerializer]
                    public record R(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(1)] public string P5 { get; set; }
                    }

                    [GenerateSerializer]
                    public record struct RS(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(1)] public string P5 { get; set; }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证分析器忽略未标记 [GenerateSerializer] 特性的类型，
    /// 即使这些类型具有重复 [Id] 特性。分析器仅验证
    /// 参与 Orleans 序列化的类型。
    /// </summary>
    [Fact]
    public Task TypesWithoutGenerateSerializerAndDuplicatedIds_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class C
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(1)] public string P3 { get; set; }
                    }

                    public struct S
                    {
                        [Id(0)] public string P1 { get; set; }
                        [Id(1)] public string P2 { get; set; }
                        [Id(1)] public string P3 { get; set; }
                    }

                    public record R(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(0)] public string P5 { get; set; }
                    }

                    public record struct RS(string P1, string P2, string P3)
                    {
                        [Id(0)] public string P4 { get; set; }
                        [Id(0)] public string P5 { get; set; }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    [Fact]
    public async Task PartialType_WithAliasedAttributes_ShouldReportExactDuplicateIdLocations()
    {
        const string firstId = "FieldId(7)";
        const string secondId = "global::Orleans.Id(7)";
        var source = $$"""
            using Serializer = Orleans.GenerateSerializerAttribute;
            using FieldId = Orleans.IdAttribute;

            [Serializer]
            public partial class C
            {
                [{{firstId}}] public int First;
                [FieldId(8)] public int Unique;
            }

            public partial class C
            {
                [{{secondId}}] public int Second;
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Collection(diagnostics,
            diagnostic => AssertDuplicateId(diagnostic, firstId),
            diagnostic => AssertDuplicateId(diagnostic, secondId));

        void AssertDuplicateId(Diagnostic diagnostic, string attribute)
        {
            Assert.Equal(IdClashAttributeAnalyzer.RuleId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(new TextSpan(source.IndexOf(attribute, StringComparison.Ordinal), attribute.Length), diagnostic.Location.SourceSpan);
            Assert.Equal("7", diagnostic.Properties["IdValue"]);
        }
    }
}
