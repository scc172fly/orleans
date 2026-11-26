using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 用于分析器的测试,以防止在 grain 类中错误使用序列属性。
/// Grain 类别(继承) Grain 或 Grain&lt;T&gt;)不应用[Alias]或
/// GenerateSerializer是因为 grains 没有序列化,而只有其状态和方法参数。
/// 此分析器有助于开发者避免混淆 grain 执行和可序列数据类型。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class IncorrectAttributeUseAnalyzerTest : DiagnosticAnalyzerTestBase<IncorrectAttributeUseAnalyzer>
{
    private async Task VerifyHasDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.NotEmpty(diagnostics);
        var diagnostic = diagnostics.First();

        Assert.Equal(IncorrectAttributeUseAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private async Task VerifyHasNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());
        Assert.Empty(diagnostics);
    }

    #region Grain

    /// <summary>
    /// 证明分析器检测到从 Grain 继承的某一类人何时继承
    /// 应用序列属性。 Grains 他们没有被连成若干级,
    /// 因此这些属性毫无意义,表明对 Orleans 模式的误解。
    /// </summary>
    [Theory]
    [MemberData(nameof(Attributes))]
    public Task ClassInheritingFromGrain_HavingAttributeApplied_ShouldTriggerDiagnostic(string attribute)
    {
        var code = $$"""
                    [{{attribute}}]
                    public class C : Grain
                    {

                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器允许正常班级的序列属性
    /// 无法继承 Grain。这些类别可以作为方法参数序列化
    /// 或 grain 状态。
    /// </summary>
    [Theory]
    [MemberData(nameof(Attributes))]
    public Task ClassNotInheritingFromGrain_HavingAttributeApplied_ShouldNotTriggerDiagnostic(string attribute)
    {
        var code = $$"""
                    [{{attribute}}]
                    public class C
                    {

                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 证明没有序列属性的 grain 类不会触发
    /// 分析器。 这是执行 grain 的正确模式 。
    /// </summary>
    [Fact]
    public Task ClassInheritingFromGrain_NotHavingAttributeApplied_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class C : Grain
                    {

                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region Grain<TGrainState>

    /// <summary>
    /// 证明分析器检测到从 Grain&lt;T&gt; 继承的某一类人何时继承
    /// 应用的序列属性。与非通用 grains、状态 grains一样
    /// 他们没有被连为系列 - 只有他们的状况。
    /// </summary>
    [Theory]
    [MemberData(nameof(Attributes))]
    public Task ClassInheritingFromGenericGrain_HavingAttributeApplied_ShouldTriggerDiagnostic(string attribute)
    {
        var code = $$"""
                    [{{attribute}}]
                    public class C : Grain<S>
                    {

                    }

                    public class S
                    {
                    
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器允许正常班级的序列属性
    /// 继承遗产 Grain&lt;T&gt;。这些类别可以作为方法参数序列
    /// 或 grain 状态。
    /// </summary>
    [Theory]
    [MemberData(nameof(Attributes))]
    public Task ClassNotInheritingFromGenericGrain_HavingAttributeApplied_ShouldNotTriggerDiagnostic(string attribute)
    {
        var code = $$"""
                    [{{attribute}}]
                    public class C
                    {

                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 证明没有序列属性的状态 grain 类不会触发
    /// 分析器。 这是显示状态的正确模式 grain 执行。
    /// </summary>
    [Fact]
    public Task ClassInheritingFromGenericGrain_NotHavingAttributeApplied_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class C : Grain<S>
                    {

                    }

                    public class S
                    {

                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    [Fact]
    public async Task AliasedAttributesOnPartialGrain_ShouldReportExactAttributeLocations()
    {
        const string alias = "StableName(\"grain\")";
        const string serializer = "Serializer";
        var source = $$"""
            using StableName = Orleans.AliasAttribute;
            using Serializer = Orleans.GenerateSerializerAttribute;

            public partial class C : Orleans.Grain { }

            [{{alias}}, {{serializer}}]
            public partial class C { }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Collection(diagnostics,
            diagnostic => AssertAttributeDiagnostic(diagnostic, source.IndexOf(alias, StringComparison.Ordinal), alias.Length),
            diagnostic => AssertAttributeDiagnostic(diagnostic, source.IndexOf(", " + serializer + "]", StringComparison.Ordinal) + 2, serializer.Length));

        static void AssertAttributeDiagnostic(Diagnostic diagnostic, int start, int length)
        {
            Assert.Equal(IncorrectAttributeUseAnalyzer.RuleId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(new TextSpan(start, length), diagnostic.Location.SourceSpan);
        }
    }

    public static IEnumerable<object[]> Attributes =>
        new List<object[]>
        {
            new object[] { "Alias(\"alias\")" },
            new object[] { "GenerateSerializer" }
        };
}
