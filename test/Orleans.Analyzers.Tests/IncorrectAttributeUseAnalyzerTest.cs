using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试禁止在 Grain 类上错误使用序列化特性的分析器。
/// Grain 类（继承 Grain 或 Grain&lt;T&gt;）不应标记 [Alias] 或
/// [GenerateSerializer]，因为不会序列化 Grain 本身，仅序列化其状态和方法参数。
/// 此分析器帮助开发者区分 Grain 实现与可序列化数据类型。
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
    /// 验证分析器能否检测继承 Grain 的类上
    /// 应用序列化特性的情况。不会序列化 Grain 本身，
    /// 因此这些特性没有意义，并且表明对 Orleans 模型存在误解。
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
    /// 验证分析器允许在不继承 Grain 的普通类上
    /// 应用序列化特性。这些类可以作为方法参数
    /// 或 Grain 状态进行序列化。
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
    /// 验证未使用序列化特性的 Grain 类不会触发
    /// 分析器诊断。这是 Grain 实现的正确模式。
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
    /// 验证分析器能否检测继承 Grain&lt;T&gt; 的类上
    /// 应用序列化特性的情况。与非泛型 Grain 一样，有状态 Grain
    /// 本身不会被序列化，只有其状态会被序列化。
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
    /// 验证分析器允许在不继承 Grain&lt;T&gt; 的普通类上
    /// 应用序列化特性。这些类可以作为方法参数
    /// 或 Grain 状态进行序列化。
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
    /// 验证未使用序列化特性的有状态 Grain 类不会触发
    /// 分析器诊断。这是有状态 Grain 实现的正确模式。
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
