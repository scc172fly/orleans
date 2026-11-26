using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 检测可序列类型中重复[ID]属性的分析器测试。
/// 在 Orleans 序号序列化中,每个字段必须有一个独 [Id] 属性值以确保
/// 重复的识别码将引起序列化
/// grains之间交换信息时发生数据冲突和数据腐败。
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
    /// 证明分析器在[GenerateSerializer]类型中多个字段时检测到
    /// 具有与序列器相同的[Id]值。 这将引起连序冲突
    /// 不知道在解密期间 地图上该绘制的标识
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
    /// 证明分析器没有报告在GenerateSerializer所有字段时出现问题
    /// 类型中具有独有的[ID]值。这是 Orleans 序列化的正确模式。
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
    /// 证明分析器忽略了没有类型的类型 [GenerateSerializer属性,
    /// 即使它们重复了[ID]属性。分析器只验证类型
    /// 参与 Orleans 序列化的。
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
