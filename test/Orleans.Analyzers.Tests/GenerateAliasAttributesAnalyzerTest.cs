using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 向类型和方法添加 [lias] 属性的 GenerateAliasAttributesAnalyzer 测试
/// Orleans 使用别名,用于在各种版本和部署中进行稳定型号识别。
/// 此分析器帮助开发者记住在 grain 接口、 可连号类型和 RPC 方法中添加别名 。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class GenerateAliasAttributesAnalyzerTest : DiagnosticAnalyzerTestBase<GenerateAliasAttributesAnalyzer>
{
    private async Task VerifyHasDiagnostic(string code, int diagnosticsCount = 1)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.NotEmpty(diagnostics);
        Assert.Equal(diagnosticsCount, diagnostics.Length);

        var diagnostic = diagnostics.First();

        Assert.Equal(GenerateAliasAttributesAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    private async Task VerifyHasNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());
        Assert.Empty(diagnostics);
    }

    #region Interfaces & Methods

    /// <summary>
    /// 证明分析器建议对 grain 接口及其方法添加[ Alis] 属性
    /// 每个界面和非静态方法都应该有别名 来进行RPC的正确路径
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task GrainInterfaceWithoutAliasAttribute_ShouldTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
                    public interface I : {{grainInterface}}
                    {
                        Task<int> M1();
                        Task<int> M2();

                        static Task<int> M3() => Task.FromResult(0);
                    }
                    """;

        return VerifyHasDiagnostic(code, 3);  // 3个诊断,因为1个接口,2个非静态方法
    }

    /// <summary>
    /// 证明当 grain 接口及其方法已经具有[Alias]属性时,分析器不会触发。
    /// </summary>
    [Theory]
    [MemberData(nameof(GrainInterfaces))]
    public Task GrainInterfaceWithAliasAttribute_ShouldNotTriggerDiagnostic(string grainInterface)
    {
        var code = $$"""
                    [Alias("I")]
                    public interface I : {{grainInterface}}
                    {
                        [Alias("M1")] Task<int> M1();
                        [Alias("M2")] Task<int> M2();

                        static Task<int> M3() => Task.FromResult(0);
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 证明分析器没有建议非grain 接口的别名,
    /// 仅需要别名 grain 中 Orleans 区域采购方案系统。
    /// </summary>
    [Fact]
    public Task NonGrainInterfaceWithoutAliasAttribute_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public interface I
                    {
                        Task<int> M1();
                        Task<int> M2();

                        static Task<int> M3() => Task.FromResult(0);
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    [Fact]
    public async Task ReferencedGrainInterfaceWithoutAliasAttribute_ShouldNotCrashAnalyzer()
    {
        const string referencedSource = """
            using Orleans;
            using System.Threading.Tasks;

            public interface IReferencedGrain : IGrainWithGuidKey
            {
                Task<int> M1();
            }
            """;

        var diagnostics = await GetDiagnosticsWithReferencedAssemblyAsync("public class C {}", referencedSource);

        Assert.Empty(diagnostics);
    }

    #endregion

    #region Classes, Structs, Records

    /// <summary>
    /// 证明分析器建议将[阿利亚斯]添加到标有GenerateSerializer的类中。
    /// 需要使用可耐版本序列化的代名词,以进行可级数式序列化。
    /// </summary>
    [Fact]
    public Task ClassWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
        => VerifyHasDiagnostic("[GenerateSerializer] public class C {}");

    /// <summary>
    /// 证明分析器建议对标有GenerateSerializer的支架添加[Alis]。
    /// </summary>
    [Fact]
    public Task StructWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public struct S {}");

    /// <summary>
    /// 证明分析器建议添加[阿利亚斯],以记录标有GenerateSerializer的分类。
    /// </summary>
    [Fact]
    public Task RecordClassWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public record R {}");

    /// <summary>
    /// 证明分析器建议添加[阿利亚斯],以记录标有GenerateSerializer的支架。
    /// </summary>
    [Fact]
    public Task RecordStructWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public record struct RS {}");

    /// <summary>
    /// 证明分析器在某类人有[ 时不会触发GenerateSerializer已经有一个[阿利亚斯]。
    /// </summary>
    [Fact]
    public Task ClassWithAliasAttribute_AndWithGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
        => VerifyHasNoDiagnostic("[GenerateSerializer, Alias(\"C\")] public class C {}");

    [Fact]
    public Task StructWithAliasAttribute_AndWithGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("[GenerateSerializer, Alias(\"S\")] public struct S {}");

    [Fact]
    public Task RecordClassWithAliasAttribute_AndWithGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("[GenerateSerializer, Alias(\"R\")] public record R {}");

    [Fact]
    public Task RecordStructWithAliasAttribute_AndWithGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("[GenerateSerializer, Alias(\"RS\")] public record struct RS {}");

    /// <summary>
    /// 证明分析器没有建议为不上课的班级使用别名[GenerateSerializer],
    /// 仅作为可连号类型需要化名。
    /// </summary>
    [Fact]
    public Task ClassWithoutAliasAttribute_AndWithoutGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
        => VerifyHasNoDiagnostic("public class C {}");

    [Fact]
    public Task StructWithoutAliasAttribute_AndWithoutGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("public struct S {}");

    [Fact]
    public Task RecordClassWithoutAliasAttribute_AndWithoutGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("public record R {}");

    [Fact]
    public Task RecordStructWithoutAliasAttribute_AndWithoutGenerateSerializerAttribute_ShouldNotTriggerDiagnostic()
       => VerifyHasNoDiagnostic("public record struct RS {}");

    #endregion

    [Fact]
    public async Task PartialTypesAndMethods_WithAliasedAttributes_ShouldOnlyReportMissingAliases()
    {
        const string unaliasedType = "[Serializer] public partial class Unaliased { }";
        const string unaliasedMethod = "System.Threading.Tasks.Task Missing();";
        var source = $$"""
            using Orleans;
            using Serializer = Orleans.GenerateSerializerAttribute;
            using StableName = Orleans.AliasAttribute;

            [Serializer] public partial class Aliased { }
            [StableName("data")] public partial class Aliased { }

            {{unaliasedType}}
            public partial class Unaliased { }

            public partial interface IApi : IGrainWithGuidKey
            {
                [StableName("existing")] System.Threading.Tasks.Task Existing();
            }

            [StableName("api")]
            public partial interface IApi
            {
                {{unaliasedMethod}}
            }
            """;

        var diagnostics = await GetDiagnosticsFullSourceAsync(source);

        Assert.Collection(diagnostics,
            diagnostic => AssertMissingAlias(diagnostic, unaliasedType, "Unaliased"),
            diagnostic => AssertMissingAlias(diagnostic, unaliasedMethod, "Missing"));

        void AssertMissingAlias(Diagnostic diagnostic, string declaration, string name)
        {
            Assert.Equal(GenerateAliasAttributesAnalyzer.RuleId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal(new TextSpan(source.IndexOf(declaration, StringComparison.Ordinal), declaration.Length), diagnostic.Location.SourceSpan);
            Assert.Equal(name, diagnostic.Properties["TypeName"]);
        }
    }

    private static async Task<Diagnostic[]> GetDiagnosticsWithReferencedAssemblyAsync(string source, string referencedSource)
    {
        static CSharpCompilation CreateCompilation(string assemblyName, string sourceText, IEnumerable<MetadataReference> references)
            => CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(sourceText)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var references = GetMetadataReferences();
        var referencedCompilation = CreateCompilation("ReferencedAssembly", referencedSource, references);

        using var stream = new MemoryStream();
        var emitResult = referencedCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var compilation = CreateCompilation("TestProject", source, [.. references, MetadataReference.CreateFromImage(stream.ToArray())]);
        var analyzer = new GenerateAliasAttributesAnalyzer();
        var compilationWithAnalyzers = compilation
            .WithOptions(
                compilation.Options.WithSpecificDiagnosticOptions(
                    analyzer.SupportedDiagnostics.ToDictionary(d => d.Id, d => ReportDiagnostic.Default)))
            .WithAnalyzers([analyzer]);

        return (await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync()).ToArray();
    }

    private static IReadOnlyCollection<MetadataReference> GetMetadataReferences()
    {
        var assemblies = new[]
        {
            typeof(Task).Assembly,
            typeof(Orleans.IGrain).Assembly,
            typeof(Orleans.Grain).Assembly,
            typeof(Attribute).Assembly,
            typeof(int).Assembly,
            typeof(object).Assembly,
        };

        var metadataReferences = assemblies
            .SelectMany(x => x.GetReferencedAssemblies().Select(Assembly.Load))
            .Concat(assemblies)
            .Distinct()
            .Select(x => MetadataReference.CreateFromFile(x.Location))
            .ToList();

        // System.Private.CoreLib 从文件支持的组装装入 。
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")));

        return metadataReferences;
    }
}
