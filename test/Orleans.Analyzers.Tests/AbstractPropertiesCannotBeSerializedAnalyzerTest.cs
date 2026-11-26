using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 确保抽象属性的 AbstractPropertiesCannotBeSerializedAnalyzer 测试
/// 无法用序列属性标记,因为它们没有具体实施序列属性。
/// 此分析器通过在编译时捕获无效的序列配置来防止运行时间错误 。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class AbstractPropertiesCannotBeSerializedAnalyzerTest : DiagnosticAnalyzerTestBase<AbstractPropertiesCannotBeSerializedAnalyzer>
{
    private async Task VerifyGeneratedDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, new string[0]);

        Assert.NotEmpty(diagnostics);
        Assert.Single(diagnostics);

        var diagnostic = diagnostics.First();
        Assert.Equal(AbstractPropertiesCannotBeSerializedAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// 证明分析器使用命名空间别名时检测到带有序列属性的抽象属性。
    /// </summary>
    [Fact]
    public Task AliasedAttribute()
        => VerifyGeneratedDiagnostic("""
using alias = Orleans;
[alias::GenerateSerializer]
public abstract class D { [alias::Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 证明分析器在使用全球合格命名空间时检测到带有序列属性的抽象特性。
    /// </summary>
    [Fact]
    public Task GloballyQualifiedAttribute()
        => VerifyGeneratedDiagnostic("""
[global::Orleans.GenerateSerializer]
public abstract class D { [global::Orleans.Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 证明分析器使用简单的属性名称时检测到带有序列特性的抽象属性。
    /// </summary>
    [Fact]
    public Task SimpleAttribute()
        => VerifyGeneratedDiagnostic("""
[GenerateSerializer] public abstract class D { [Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 证明分析器正确识别带有序列属性的抽象属性
    /// 即便存在其他无关的通用属性。
    /// </summary>
    [Fact]
    public Task UnrelatedGenericAttribute()
        => VerifyGeneratedDiagnostic("""
public class GenericAttribute<T> : Attribute { }
[GenerateSerializer]
public abstract class D {
    [GenericAttribute<bool>] [Id(0)] public abstract int F { get; set; }
}
""");

    [Fact]
    public async Task PartialSerializableType_ShouldReportOnlyAbstractAndStaticIdAttributeSpans()
    {
        const string abstractId = "Id(0)";
        const string staticId = "Id(1)";
        var source = $$"""
            using Orleans;
            using Serializer = Orleans.GenerateSerializerAttribute;

            [Serializer] public abstract partial class D { }

            public abstract partial class D
            {
                [{{abstractId}}] public abstract int Abstract { get; set; }
                [{{staticId}}] public static int Static { get; set; }
                [Id(2)] public int Concrete { get; set; }
            }

            public abstract class NotSerializable
            {
                [Id(3)] public abstract int Value { get; set; }
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Collection(diagnostics,
            diagnostic => AssertInvalidMember(diagnostic, abstractId),
            diagnostic => AssertInvalidMember(diagnostic, staticId));

        void AssertInvalidMember(Diagnostic diagnostic, string attribute)
        {
            Assert.Equal(AbstractPropertiesCannotBeSerializedAnalyzer.RuleId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(new TextSpan(source.IndexOf(attribute, StringComparison.Ordinal), attribute.Length), diagnostic.Location.SourceSpan);
        }
    }
}
