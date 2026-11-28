using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGenerator.Tests.ReferencedTaggedUnions;
using Orleans.Serialization;
using Xunit;
using static Orleans.CodeGenerator.Tests.TaggedUnionTestCompilation;

namespace Orleans.CodeGenerator.Tests;

/// <summary>
/// 测试所有受支持编译器都必须提供的联合类型处理功能。
/// </summary>
/// <remarks>
/// net8.0 测试在支持的最旧 Roslyn 版本（没有联合类型符号 API）上运行生成器；net10.0 测试在具有这些 API 的 Roslyn 版本上运行生成器。
/// </remarks>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("CodeGen")]
public sealed class TaggedUnionCompilerCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencedUnionsUseRegisteredCopiersWithoutCaseMetadata(bool hotReload)
    {
        var producerAssembly = typeof(ReferencedShallowUnion).Assembly;
        var producerReference = MetadataReference.CreateFromFile(producerAssembly.Location);
        var (output, result) = Generate("""
            using Orleans.CodeGenerator.Tests.ReferencedTaggedUnions;

            [GenerateSerializer]
            public sealed class Holder : IReferencedUnionHolder
            {
                [Id(0)] public ReferencedShallowUnion Shallow { get; set; }
                [Id(1)] public ReferencedDeepUnion Deep { get; set; }
                [Id(2)] public ReferencedDeepUnion Alias { get; set; }
                [Id(3)] public ReferencedImmutableUnion Immutable { get; set; }
            }
            """, hotReload, producerReference);

        var producerUnion = output.GetTypeByMetadataName(typeof(ReferencedShallowUnion).FullName!)!;
        Assert.True(LanguageFeatureSymbols.IsUnion(producerUnion));
        var unavailableCases = Assert.Throws<OrleansGeneratorDiagnosticAnalysisException>(() => LanguageFeatureSymbols.GetUnionCaseTypes(producerUnion));
        Assert.Equal("ORLEANS0117", unavailableCases.Diagnostic.Id);
        Assert.Null(producerAssembly.GetType("OrleansCodeGen.Orleans.CodeGenerator.Tests.ReferencedTaggedUnions.Copier_ReferencedShallowUnion"));
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Id == "ORLEANS0117");
        AssertNoErrors(output);
        var copier = GetCodec(result, "Copier_Holder").NormalizeWhitespace().ToFullString();
        Assert.Contains("IDeepCopier<global::Orleans.CodeGenerator.Tests.ReferencedTaggedUnions.ReferencedShallowUnion>", copier, StringComparison.Ordinal);
        Assert.Contains("IDeepCopier<global::Orleans.CodeGenerator.Tests.ReferencedTaggedUnions.ReferencedDeepUnion>", copier, StringComparison.Ordinal);
        Assert.DoesNotContain("Copier_ReferencedShallowUnion", copier, StringComparison.Ordinal);

        using var image = new MemoryStream();
        var emitted = output.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var loadContext = new AssemblyLoadContext(nameof(ReferencedUnionsUseRegisteredCopiersWithoutCaseMetadata), isCollectible: true);
        try
        {
            var consumerAssembly = loadContext.LoadFromStream(image);
            var holder = (IReferencedUnionHolder)Activator.CreateInstance(consumerAssembly.GetType("Holder", throwOnError: true)!)!;
            var values = new List<int> { 7, 11 };
            holder.Shallow = 42;
            holder.Deep = values;
            holder.Alias = values;
            holder.Immutable = values;
            using var services = new ServiceCollection()
                .AddSerializer(builder => builder.AddAssembly(producerAssembly).AddAssembly(consumerAssembly))
                .BuildServiceProvider();

            var copy = (IReferencedUnionHolder)services.GetRequiredService<DeepCopier>().Copy((object)holder);

            Assert.NotSame(holder, copy);
            Assert.Equal(42, copy.Shallow.Value);
            var copiedValues = Assert.IsType<List<int>>(copy.Deep.Value);
            Assert.Equal(new[] { 7, 11 }, copiedValues);
            Assert.NotSame(values, copiedValues);
            Assert.Same(copiedValues, copy.Alias.Value);
            Assert.Same(values, copy.Immutable.Value);
            copiedValues[0] = 101;
            Assert.Equal(new[] { 7, 11 }, values);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void CustomUnionWithoutCompilerCaseTypesReportsDiagnostic()
    {
        // 按字段序列化的自定义联合类型会写出与较新编译器生成的带标记格式不同的格式。
        var (_, result) = Generate("""
            [System.Runtime.CompilerServices.Union, GenerateSerializer]
            public readonly struct CustomUnion : System.Runtime.CompilerServices.IUnion
            {
                public CustomUnion(int value) => Value = value;
                public CustomUnion(string value) => Value = value;
                public object? Value { get; }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static diagnostic => diagnostic.Id == "ORLEANS0117");
        Assert.Contains("The compiler does not expose the case types of union type 'CustomUnion' to source generators", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
