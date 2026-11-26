using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Orleans.CodeGenerator.Tests;

/// <summary>
/// 直接写入生成文件的测试保持了发电机一直产生的正常布局,
/// 并编集序列、代理、可引用、元数据和上下文产出。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("CodeGen")]
public sealed class GeneratedSourceLayoutTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Orleans;

        [assembly: RegisterProvider("Layout\"Provider", "Kind\\One", "Ünïcødé", typeof(Layout.ProviderConfigurator))]

        namespace Layout
        {
            public sealed class ProviderConfigurator { }

            [GenerateSerializer, Alias("layout-context-root")]
            public sealed class ContextRoot { [Id(0)] public List<int> Values; [Id(1)] public ContextChild Nested; }

            [GenerateSerializer, Id(72)]
            public sealed class ContextChild { [Id(0)] public string Name; }

            [GenerateSerializer, Alias("layout-alias \"quoted\" \\ é"), Id(71)]
            public sealed class Aliased { [Id(0)] public int Value; }

            [GenerateSerializer]
            public enum @event : long { Minimum = long.MinValue, Maximum = long.MaxValue }

            [GenerateSerializer]
            public class Constrained<TKey, TValue> where TKey : class, new() where TValue : struct
            {
                [Id(0)] public Dictionary<TKey, TValue[]> Values;
                [Id(1)] public TValue?[,] Grid;
                [Id(2)] public (int Count, string Name) Tuple;
            [Id(3)] public (int[] Items, int? Count, List<int> Values) Shape;
            }

            public class Outer<T>
            {
                [GenerateSerializer]
                public sealed class Inner<U> { [Id(0)] public T First; [Id(1)] public U Second; }
            }

            [GenerateSerializer]
            public record class RecordPayload([property: Id(0)] string Name, [property: Id(1)] int Count);

            [GenerateSerializer]
            public readonly record struct ReadOnlyRecordStructPayload([property: Id(0)] int Value);

            [GenerateSerializer]
            public readonly struct ReadOnlyStructWithArray
            {
                [Id(0)] private readonly int[] _values;
                public ReadOnlyStructWithArray(int[] values) => _values = values;
            }

            [GenerateSerializer]
            public class Keywords
            {
                [Id(0)] public int @class;
                [Id(1)] public string @value;
                [Id(2)] private int _hidden;
                [Id(3)] public int Prop { get; private set; }
                public int Hidden => _hidden;
            }

            [GenerateSerializer]
            public abstract class Base { [Id(0)] public Keywords Shared; }

            [GenerateSerializer, OmitDefaultMemberValues]
            public class OmitDefaults : Base { [Id(0)] public int Number; [Id(1)] public string Text; }

            [GenerateSerializer, Immutable]
            public class UnsealedImmutable { [Id(0)] public int Value; }

            [GenerateSerializer, UseActivator]
            public sealed class Activated { [Id(0)] public int Value; }

            [GenerateSerializer, SerializationCallbacks(typeof(Hooks))]
            public sealed class WithHooks { [Id(0)] public int Value; }

            public sealed class Hooks
            {
                public void OnDeserializing(WithHooks value) { }
                public void OnCopied(WithHooks original, WithHooks result) { }
            }

            [GenerateSerializer]
            public sealed class PayloadException : Exception { [Id(0)] public Keywords Detail; }

            public interface ILayoutBase : IGrainWithStringKey { Task Shared(int value); }
            public interface ILeft : ILayoutBase { Task<int> Left(); }
            public interface IRight : ILayoutBase { ValueTask Right(CancellationToken cancellationToken); }

            [Alias("layout.grain")]
            public interface ILayoutGrain<T> : ILeft, IRight where T : class
            {
                [ResponseTimeout("00:00:05")]
                Task<T> Get<TResult>(T @event, TResult value, CancellationToken cancellationToken) where TResult : struct, IEquatable<TResult>;
                ValueTask<int> Count(Constrained<Keywords, int> payload, Keywords[] keywords);
                Task Overload(int value);
                Task Overload(int value, CancellationToken cancellationToken);
            }

            public interface ILayoutObserver : IGrainObserver { void Notify(string message); }

            [GeneratedContextType<ContextRoot>, GeneratedContextType<int?[]>]
            public sealed partial class LayoutContext : Orleans.Serialization.GeneratedContext { }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedFilesKeepNormalizedLayoutAndCompile(bool hotReload)
    {
        // 供应商登记即刻通知元数据提供者,其选择合同由 Microsoft.Extensions.Options申报。
        var compilation = await TestCompilationHelper.CreateCompilation(Source, "TestProject",
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IConfigureOptions<>).Assembly.Location));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new OrleansSerializationSourceGenerator().AsSourceGenerator()],
            optionsProvider: TestCompilationHelper.CreateOptionsProvider(hotReload
                ? new Dictionary<string, string> { ["build_property.orleanshotreload"] = "true" }
                : null));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, TestContext.Current.CancellationToken);
        Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Contains(sources, static source => source.HintName.Contains(".orleans.proxy.", StringComparison.Ordinal));
        Assert.Contains(sources, static source => source.HintName.Contains(".orleans.ser.", StringComparison.Ordinal));
        Assert.Contains(sources, static source => source.HintName.Contains(".orleans.metadata.", StringComparison.Ordinal));
        Assert.Contains(sources, static source => source.HintName.Contains(".orleans.context.", StringComparison.Ordinal));
        Assert.Contains(sources, static source => source.HintName.Contains(".orleans.context-declaration.", StringComparison.Ordinal));
        foreach (var source in sources)
        {
            var text = source.SourceText.ToString();
            if (text.Length == 0)
            {
                continue;
            }

            Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.False(text.EndsWith("\r\n", StringComparison.Ordinal), source.HintName);
            var body = text;
            const string Disable = "#pragma warning disable\r\n";
            const string Restore = "\r\n#pragma warning restore";
            if (body.StartsWith(Disable, StringComparison.Ordinal))
            {
                Assert.EndsWith(Restore, body, StringComparison.Ordinal);
                body = body[Disable.Length..^Restore.Length];
            }

            // 生成的文件使用Roslyn的普通语句布局, 带有异域符号拼写, 其常规化不同 :
            // “T”和“T? []” ,而不是“T? ]”和“T? [ ]”,“G<,>”和“int[,] ”而不是“G<, >”和“int[, ] ”,
            // 而不是"不",还有它声明的每个限制条款
            var normalized = Idiomatic(SyntaxFactory.ParseCompilationUnit(body).NormalizeWhitespace().ToFullString());
            if (!string.Equals(normalized, body, StringComparison.Ordinal))
            {
                var expectedLines = normalized.Split("\r\n");
                var actualLines = body.Split("\r\n");
                var line = 0;
                while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
                {
                    line++;
                }

                Assert.Fail($"{source.HintName} differs from its normalized layout at line {line + 1}:{Environment.NewLine}" +
                    $"normalized: {(line < expectedLines.Length ? expectedLines[line] : "<end>")}{Environment.NewLine}" +
                    $"generated:  {(line < actualLines.Length ? actualLines[line] : "<end>")}");
            }
        }

        var metadata = Assert.Single(sources, static source => source.HintName.EndsWith(".orleans.metadata.g.cs", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("providers[(\"Silo\", \"Kind\\\\One\", \"Layout\\\"Provider\")]", metadata.Replace("Ünïcødé", "Silo", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("config.WellKnownTypeAliases.TryGetValue(\"layout-alias \\\"quoted\\\" \\\\ é\"", metadata, StringComparison.Ordinal);
        Assert.Contains("config.WellKnownTypeIds.TryGetValue(71U,", metadata, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Layout.Constrained<,>)", metadata, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Layout.Outer<>.Inner<>)", metadata, StringComparison.Ordinal);

        var serializers = string.Concat(sources.Where(static source => source.HintName.Contains(".orleans.ser.", StringComparison.Ordinal))
            .Select(static source => source.SourceText.ToString()));
        Assert.Contains("(int[] Items, int? Count, global::System.Collections.Generic.List<int> Values)", serializers, StringComparison.Ordinal);
        Assert.Contains("TValue?[,]", serializers, StringComparison.Ordinal);

        var context = Assert.Single(sources, static source => source.HintName.Contains(".orleans.context.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("options.AddSerializer<int?[]>(", context, StringComparison.Ordinal);
    }

    private static string Idiomatic(string normalized)
        => Regex.Replace(normalized.Replace("? )", "?)", StringComparison.Ordinal)
                .Replace("? [", "?[", StringComparison.Ordinal)
                .Replace("null !", "null!", StringComparison.Ordinal),
            @", (?=[,>\]])|(?<=new\(\))\r\n *(?=where )", static match => match.Value.StartsWith(',') ? "," : " ");
}
