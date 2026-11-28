using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 测试 <c>DurableTask.RunInline</c> 接受同步效果，并在编译
/// 时拒绝它无法记录的异步及持久化效果。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class DurableTaskRunInlineOverloadTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. new[] { typeof(object).Assembly, typeof(Task).Assembly, typeof(DurableTask).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies().Select(Assembly.Load).Append(assembly))
            .Distinct()
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)),
    ];

    public static TheoryData<string> SynchronousEffects => new()
    {
        "int value = DurableTask.RunInline(\"a\", () => 1);",
        "DurableTask.RunInline(\"a\", () => { });",
        "Guid id = DurableTask.RunInline(\"a\", Guid.NewGuid);",
        "DurableTask.RunInline(\"a\", () => { if (Environment.ProcessorCount > 0) throw new InvalidOperationException(); });",
        "int value = DurableTask.RunInline(\"a\", Compute);",
        "int counter = 0; int value = DurableTask.RunInline(\"a\", () => counter++);",
        "int value = DurableTask.RunInline(() => 1);",
        "DurableTask.RunInline(() => { });",
        "Guid id = DurableTask.RunInline(Guid.NewGuid);",
        "DurableTask.RunInline(() => { if (Environment.ProcessorCount > 0) throw new InvalidOperationException(); });",
        "int value = DurableTask.RunInline(Compute);",
        "string value = DurableTask.RunInline(() => \"a\");",
        "DurableTask.RunInline(Mark);",
        "DurableTask.RunInline(() => Mark());",

        // 表达式具有返回值的 Lambda 可以转换为 Action 或 Func<T>；效果返回该值。
        "int counter = 0; int value = DurableTask.RunInline(() => counter++);",
        "int value = DurableTask.RunInline(() => Compute());",
        "_ = DurableTask.RunInline(() => 1);",

        // 仅抛出异常的 Lambda 也可以转换为 Func<Task>，并匹配拒绝此委托的重载；显式转换为 Action 可选择相应重载。
        "DurableTask.RunInline((Action)(() => throw new InvalidOperationException()));",
        "DurableTask.RunInline(\"a\", (Action)(() => { throw new InvalidOperationException(); }));",

        // 显式类型实参选择同步重载，其结果类型检查会在效果运行时拒绝该效果。
        "_ = DurableTask.RunInline<Task<int>>(() => Task.FromResult(1));",
    };

    private const string UseRun = "For asynchronous work, use await DurableTask.Run(async () => ...).";
    private const string AwaitDirectly = "Await the durable task directly.";

    public static TheoryData<string, string> AsynchronousEffects => new()
    {
        { "_ = DurableTask.RunInline(\"a\", async () => { await Task.Yield(); return 1; });", UseRun },
        { "DurableTask.RunInline(\"a\", async () => await Task.Yield());", UseRun },
        { "_ = DurableTask.RunInline(\"a\", () => Task.FromResult(1));", UseRun },
        { "_ = DurableTask.RunInline(\"a\", ComputeAsync);", UseRun },
        { "_ = DurableTask.RunInline(\"a\", () => DurableTask.FromResult(1));", AwaitDirectly },
        { "_ = DurableTask.RunInline(\"a\", Step);", AwaitDirectly },
        { "_ = DurableTask.RunInline(async () => { await Task.Yield(); return 1; });", UseRun },
        { "DurableTask.RunInline(async () => await Task.Yield());", UseRun },
        { "DurableTask.RunInline(async () => { });", UseRun },
        { "_ = DurableTask.RunInline(() => Task.FromResult(1));", UseRun },
        { "DurableTask.RunInline(() => Task.CompletedTask);", UseRun },
        { "_ = DurableTask.RunInline(ComputeAsync);", UseRun },
        { "_ = DurableTask.RunInline(() => DurableTask.FromResult(1));", AwaitDirectly },
        { "_ = DurableTask.RunInline(Step);", AwaitDirectly },
        { "DurableTask.RunInline(Run);", AwaitDirectly },
        { "_ = DurableTask.RunInline(() => ValueTask.FromResult(1));", UseRun },
        { "_ = DurableTask.RunInline(\"a\", () => ValueTask.FromResult(1));", UseRun },
        { "_ = DurableTask.RunInline(ComputeValueAsync);", UseRun },
        { "DurableTask.RunInline(() => ValueTask.CompletedTask);", UseRun },
        { "DurableTask.RunInline(\"a\", MarkValueAsync);", UseRun },

        // 无返回结果的异步 Lambda 也可转换为 Action，此时将以 async void 运行。
        { "DurableTask.RunInline(\"a\", async () => { await Task.Yield(); });", UseRun },

        // 仅抛出异常的 Lambda 也可转换为 Func<Task>；将其显式转换为 Action 可记录该异常。
        { "DurableTask.RunInline(() => throw new InvalidOperationException());", UseRun },
        { "DurableTask.RunInline(\"a\", () => { throw new InvalidOperationException(); });", UseRun },
    };

    [Theory]
    [MemberData(nameof(SynchronousEffects))]
    public void SynchronousEffectsCompile(string statement)
    {
        Assert.Empty(GetErrors(statement));
    }

    [Theory]
    [MemberData(nameof(AsynchronousEffects))]
    public void AsynchronousAndDurableEffectsFailWithGuidance(string statement, string guidance)
    {
        var error = Assert.Single(GetErrors(statement));
        Assert.Equal("CS0619", error.Id);
        Assert.Contains("DurableTask.RunInline stores a synchronous result", error.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(guidance, error.GetMessage(), StringComparison.Ordinal);
    }

    private static Diagnostic[] GetErrors(string statement)
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Orleans.Durable.Tasks;

            public class C
            {
                public void M()
                {
                    {{statement}}
                }

                private static int Compute() => 1;

                private static void Mark() { }

                private static Task<int> ComputeAsync() => Task.FromResult(1);

                private static ValueTask<int> ComputeValueAsync() => ValueTask.FromResult(1);

                private static ValueTask MarkValueAsync() => ValueTask.CompletedTask;

                private static async DurableTask<int> Step() => 1;

                private static async DurableTask Run() { }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path: "Test.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    }
}
