using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试 AbstractPropertiesCannotBeSerializedAnalyzer，该分析器确保抽象属性
/// 无法标记序列化特性，因为它们没有可供序列化的具体实现。
/// 此分析器在编译时捕获无效的序列化配置，从而防止运行时错误。
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
    /// 验证使用命名空间别名时，分析器能否检测带序列化特性的抽象属性。
    /// </summary>
    [Fact]
    public Task AliasedAttribute()
        => VerifyGeneratedDiagnostic("""
using alias = Orleans;
[alias::GenerateSerializer]
public abstract class D { [alias::Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 验证使用全局限定命名空间时，分析器能否检测带序列化特性的抽象属性。
    /// </summary>
    [Fact]
    public Task GloballyQualifiedAttribute()
        => VerifyGeneratedDiagnostic("""
[global::Orleans.GenerateSerializer]
public abstract class D { [global::Orleans.Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 验证使用简单特性名称时，分析器能否检测带序列化特性的抽象属性。
    /// </summary>
    [Fact]
    public Task SimpleAttribute()
        => VerifyGeneratedDiagnostic("""
[GenerateSerializer] public abstract class D { [Id(0)] public abstract int F { get; set; } }
""");

    /// <summary>
    /// 验证分析器能否正确识别带有序列化特性的抽象属性，
    /// 即使同时存在其他无关的泛型特性。
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
