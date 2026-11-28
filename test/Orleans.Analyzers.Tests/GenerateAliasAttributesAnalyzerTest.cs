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
/// 测试 GenerateAliasAttributesAnalyzer，该分析器建议为需要别名的类型与方法
/// 添加 [Alias] 特性。Orleans 使用别名在不同版本与部署中稳定标识类型。
/// 此分析器提醒开发者为 Grain 接口、可序列化类型及 RPC 方法添加别名。
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
    /// 验证在 Grain 接口及其方法没有 [Alias] 特性时，分析器会建议
    /// 添加该特性。为了正确路由 RPC，每个接口及非静态方法均应具有别名。
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

        return VerifyHasDiagnostic(code, 3);  // 3 条诊断：接口 1 条，两个非静态方法各 1 条
    }

    /// <summary>
    /// 验证 Grain 接口及其方法已经具有 [Alias] 特性时不会触发分析器诊断。
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
    /// 验证分析器不会建议为非 Grain 接口添加别名，
    /// 因为在 Orleans RPC 系统中，接口别名仅为 Grain 接口所需。
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
    /// 验证分析器建议为标记了 [GenerateSerializer] 的类添加 [Alias]。
    /// 可序列化类型需要别名，以支持具有版本容错能力的序列化。
    /// </summary>
    [Fact]
    public Task ClassWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
        => VerifyHasDiagnostic("[GenerateSerializer] public class C {}");

    /// <summary>
    /// 验证分析器建议为标记了 [GenerateSerializer] 的结构体添加 [Alias]。
    /// </summary>
    [Fact]
    public Task StructWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public struct S {}");

    /// <summary>
    /// 验证分析器建议为标记了 [GenerateSerializer] 的记录类添加 [Alias]。
    /// </summary>
    [Fact]
    public Task RecordClassWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public record R {}");

    /// <summary>
    /// 验证分析器建议为标记了 [GenerateSerializer] 的记录结构体添加 [Alias]。
    /// </summary>
    [Fact]
    public Task RecordStructWithoutAliasAttribute_AndWithGenerateSerializerAttribute_ShouldTriggerDiagnostic()
       => VerifyHasDiagnostic("[GenerateSerializer] public record struct RS {}");

    /// <summary>
    /// 验证带 [GenerateSerializer] 的类已经具有 [Alias] 时不会触发分析器诊断。
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
    /// 验证分析器不会建议为没有 [GenerateSerializer] 的类添加别名，
    /// 因为仅可序列化类型需要别名。
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

        // System.Private.CoreLib 是从基于文件的程序集加载的。
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")));

        return metadataReferences;
    }
}
