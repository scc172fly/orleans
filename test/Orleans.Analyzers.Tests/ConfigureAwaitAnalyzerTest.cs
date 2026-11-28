using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

/// <summary>
/// 测试在 Grain 代码中使用 ConfigureAwait(false) 或使用未包含
/// ContinueOnCapturedContext 的 ConfigureAwait 时发出警告的分析器。Grain 必须维持同步上下文，
/// 以确保代码在 Grain 的激活上下文中正确执行。
/// </summary>
[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class ConfigureAwaitAnalyzerTest : DiagnosticAnalyzerTestBase<ConfigureAwaitAnalyzer>
{
    private static readonly string[] Usings = new[] {
        "System",
        "System.Threading.Tasks",
        "Orleans"
    };

    private async Task VerifyHasDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.NotEmpty(diagnostics);
        var diagnostic = diagnostics.First();

        Assert.Equal(ConfigureAwaitAnalyzer.RuleId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    private async Task VerifyHasNoDiagnostic(string code)
    {
        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());
        Assert.Empty(diagnostics);
    }

    private async Task VerifyCodeFix(string originalCode, string expectedFixedCode, string[]? extraUsings = null)
    {
        extraUsings ??= Array.Empty<string>();

        // 在前面添加 using 指令
        var sb = new StringBuilder();
        foreach (var @using in Usings.Concat(extraUsings))
        {
            sb.AppendLine($"using {@using};");
        }
        sb.AppendLine(originalCode);
        var fullOriginalCode = sb.ToString();

        sb.Clear();
        foreach (var @using in Usings.Concat(extraUsings))
        {
            sb.AppendLine($"using {@using};");
        }
        sb.AppendLine(expectedFixedCode);
        var fullExpectedCode = sb.ToString();

        // 创建项目并获取诊断
        var project = CreateProject(fullOriginalCode);
        var document = project.Documents.First();
        // C# 项目始终支持编译。
        var compilation = (await project.GetCompilationAsync())!;

        var analyzer = new ConfigureAwaitAnalyzer();
        var compilationWithAnalyzers = compilation
            .WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                analyzer.SupportedDiagnostics.ToDictionary(d => d.Id, d => ReportDiagnostic.Default)))
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.NotEmpty(diagnostics);

        // 应用代码修复
        var codeFixer = new ConfigureAwaitCodeFix();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostics.First(),
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await codeFixer.RegisterCodeFixesAsync(context);
        Assert.NotEmpty(actions);

        var operations = await actions.First().GetOperationsAsync(CancellationToken.None);
        var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        // 应用代码修复时保留原始文档。
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var changedText = await changedDocument.GetTextAsync();

        Assert.Equal(fullExpectedCode, changedText.ToString());
    }

    private static Project CreateProject(string source)
    {
        const string fileName = "Test.cs";

        var projectId = ProjectId.CreateNewId(debugName: "TestProject");
        var documentId = DocumentId.CreateNewId(projectId, fileName);

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
            .Cast<MetadataReference>()
            .ToList();

        // System.Private.CoreLib 是从基于文件的程序集加载的。
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")));
        metadataReferences.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")));

        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .AddMetadataReferences(projectId, metadataReferences)
            .AddDocument(documentId, fileName, SourceText.From(source));

        // 上方已将项目添加到此解决方案。
        return solution.GetProject(projectId)!
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #region ConfigureAwait(false) in Grain

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证实现 IAddressable 接口的 Grain 类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InGrainClassWithIAddressableInterface_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : Orleans.Runtime.IAddressable
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证实现 IGrainBase 的 POCO Grain 类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InPocoGrainClassImplementingIGrainBase_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : IGrainBase, IMyGrain
                    {
                        public Orleans.Runtime.IGrainContext GrainContext { get; }
                        public MyGrain(Orleans.Runtime.IGrainContext context) => GrainContext = context;

                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : Orleans.Runtime.IAddressable
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证带 GrainType 特性的 Grain 类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InGrainClassWithGrainTypeAttribute_ShouldTriggerDiagnostic()
    {
        var code = """
                    [GrainType("my-custom-grain")]
                    public class MyGrain : IGrainBase
                    {
                        public Orleans.Runtime.IGrainContext GrainContext { get; }
                        public MyGrain(Orleans.Runtime.IGrainContext context) => GrainContext = context;

                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证泛型 Grain 类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InGenericGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain<MyState>, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public class MyState { }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(true) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitTrue_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(true);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region ConfigureAwait(false) in non-grain class

    /// <summary>
    /// 验证普通类（无继承）中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InPlainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证实现非 Grain 接口的类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InClassImplementingNonGrainInterface_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public interface IMyService
                    {
                        Task DoSomething();
                    }

                    public class MyService : IMyService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证继承非 Grain 基类的类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InClassInheritingNonGrainBase_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class BaseService
                    {
                    }

                    public class MyService : BaseService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证具有多层非 Grain 继承关系的类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InClassWithDeepNonGrainInheritance_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class GrandparentService
                    {
                    }

                    public class ParentService : GrandparentService
                    {
                    }

                    public class MyService : ParentService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证结构体中的 ConfigureAwait(false) 不会触发诊断。
    /// 结构体不能用作 Grain。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InStruct_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public struct MyStruct
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证记录类型中的 ConfigureAwait(false) 不会触发诊断。
    /// 记录类型不能用作 Grain（它们不继承 Grain 或实现 IGrainBase）。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InRecord_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public record MyRecord
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证记录结构体中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InRecordStruct_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public record struct MyRecordStruct
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证实现 IDisposable（非 Grain 接口）的类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InClassImplementingIDisposable_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyService : IDisposable
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }

                        public void Dispose() { }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证静态类中的 ConfigureAwait(false) 不会触发诊断。
    /// 静态类不能用作 Grain。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InStaticClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public static class MyStaticHelper
                    {
                        public static async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证抽象非 Grain 类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InAbstractNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public abstract class MyAbstractService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证泛型非 Grain 类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InGenericNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGenericService<T>
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证非 Grain 类的嵌套类中的 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InNestedClassInsideNonGrain_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class OuterService
                    {
                        public class InnerService
                        {
                            public async Task DoSomething()
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                            }
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证未包含 ContinueOnCapturedContext 的 ConfigureAwait(ConfigureAwaitOptions)
    /// 在非 Grain 类中不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_ForceYielding_InNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region No ConfigureAwait

    /// <summary>
    /// 验证 Grain 类中未使用 ConfigureAwait 的 await 不会触发诊断。
    /// </summary>
    [Fact]
    public Task NoConfigureAwait_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region IGrainBase implementation

    /// <summary>
    /// 验证实现 IGrainBase 的类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InIGrainBaseImplementation_ShouldTriggerDiagnostic()
    {
        var code = """
                    using Orleans.Runtime;

                    public class MyGrain : IGrainBase, IMyGrain
                    {
                        public IGrainContext GrainContext { get; }

                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    #endregion

    #region ISystemTarget implementation

    /// <summary>
    /// 验证实现 ISystemTarget 的类中的 ConfigureAwait(false) 会触发诊断。
    /// ISystemTarget 位于 Orleans 命名空间（不是 Orleans.Runtime），定义在 Orleans.Core.Abstractions 中。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InISystemTargetImplementation_ShouldTriggerDiagnostic()
    {
        // 注意：ISystemTarget 定义在 Orleans 命名空间（Orleans.Core.Abstractions 程序集）中，
        // 因此不需要额外的 using 指令，因为已经有 "using Orleans;"。
        var code = """
                    public class MySystemTarget : ISystemTarget
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    #endregion

    #region Nested classes and lambdas

    /// <summary>
    /// 验证 Grain 类中的 Lambda 表达式使用 ConfigureAwait(false) 时会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InLambdaInsideGrain_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            Func<Task> action = async () =>
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                            };
                            return action();
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类的嵌套类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InNestedClassInsideGrain_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething() => Task.CompletedTask;

                        private class NestedClass
                        {
                            public async Task DoWork()
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                            }
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        // 嵌套类位于 Grain 类内，因此仍应触发诊断。
        return VerifyHasDiagnostic(code);
    }

    #endregion

    #region Inherited grain classes

    /// <summary>
    /// 验证继承其他 Grain 类的类中的 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InInheritedGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class BaseGrain : Grain
                    {
                    }

                    public class MyGrain : BaseGrain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    #endregion

    #region ValueTask

    /// <summary>
    /// 验证 Grain 类对 ValueTask 调用 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnValueTask_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await GetValueAsync().ConfigureAwait(false);
                        }

                        private ValueTask GetValueAsync() => ValueTask.CompletedTask;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类对 ValueTask&lt;T&gt; 调用 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnGenericValueTask_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            var result = await GetValueAsync().ConfigureAwait(false);
                        }

                        private ValueTask<int> GetValueAsync() => ValueTask.FromResult(42);
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类对 ValueTask 调用 ConfigureAwait(true) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitTrue_OnValueTask_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await GetValueAsync().ConfigureAwait(true);
                        }

                        private ValueTask GetValueAsync() => ValueTask.CompletedTask;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证非 Grain 类对 ValueTask 调用 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnValueTask_InNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyService
                    {
                        public async Task DoSomething()
                        {
                            await GetValueAsync().ConfigureAwait(false);
                        }

                        private ValueTask GetValueAsync() => ValueTask.CompletedTask;
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region IAsyncEnumerable

    /// <summary>
    /// 验证 Grain 类在 await foreach 中对 IAsyncEnumerable 调用 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnIAsyncEnumerable_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Collections.Generic;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await foreach (var item in GetItemsAsync().ConfigureAwait(false))
                            {
                                // Process item
                            }
                        }

                        private async IAsyncEnumerable<int> GetItemsAsync()
                        {
                            yield return 1;
                            await Task.Delay(1);
                            yield return 2;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类在 await foreach 中对 IAsyncEnumerable 调用 ConfigureAwait(true) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitTrue_OnIAsyncEnumerable_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Collections.Generic;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await foreach (var item in GetItemsAsync().ConfigureAwait(true))
                            {
                                // Process item
                            }
                        }

                        private async IAsyncEnumerable<int> GetItemsAsync()
                        {
                            yield return 1;
                            await Task.Delay(1);
                            yield return 2;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证非 Grain 类对 IAsyncEnumerable 调用 ConfigureAwait(false) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnIAsyncEnumerable_InNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Collections.Generic;

                    public class MyService
                    {
                        public async Task DoSomething()
                        {
                            await foreach (var item in GetItemsAsync().ConfigureAwait(false))
                            {
                                // Process item
                            }
                        }

                        private async IAsyncEnumerable<int> GetItemsAsync()
                        {
                            yield return 1;
                            await Task.Delay(1);
                            yield return 2;
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类中未使用 ConfigureAwait 的 await foreach 不会触发诊断。
    /// </summary>
    [Fact]
    public Task NoConfigureAwait_OnIAsyncEnumerable_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Collections.Generic;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await foreach (var item in GetItemsAsync())
                            {
                                // Process item
                            }
                        }

                        private async IAsyncEnumerable<int> GetItemsAsync()
                        {
                            yield return 1;
                            await Task.Delay(1);
                            yield return 2;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region Task<T>

    /// <summary>
    /// 验证 Grain 类对 Task&lt;T&gt; 调用 ConfigureAwait(false) 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_OnGenericTask_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            var result = await Task.FromResult(42).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类对 Task&lt;T&gt; 调用 ConfigureAwait(true) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitTrue_OnGenericTask_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            var result = await Task.FromResult(42).ConfigureAwait(true);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region ConfigureAwait(ConfigureAwaitOptions)

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(ConfigureAwaitOptions.None) 会触发诊断，
    /// 因为它未包含 ContinueOnCapturedContext。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_None_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.None);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(ConfigureAwaitOptions.ForceYielding) 会触发诊断，
    /// 因为它未包含 ContinueOnCapturedContext。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_ForceYielding_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing) 会触发诊断，
    /// 因为它未包含 ContinueOnCapturedContext。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_SuppressThrowing_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证 Grain 类中的 ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)
    /// 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_ContinueOnCapturedContext_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证使用包含 ContinueOnCapturedContext 的组合标志调用 ConfigureAwait 时
    /// 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_CombinedWithContinueOnCapturedContext_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证使用不包含 ContinueOnCapturedContext 的组合标志调用 ConfigureAwait 时
    /// 会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_CombinedWithoutContinueOnCapturedContext_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证非 Grain 类中的 ConfigureAwait(ConfigureAwaitOptions) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_None_InNonGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyService
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.None);
                        }
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    /// <summary>
    /// 验证对 Task&lt;T&gt; 调用 ConfigureAwait(ConfigureAwaitOptions) 时能够正常处理。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_None_OnGenericTask_InGrainClass_ShouldTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            var result = await Task.FromResult(42).ConfigureAwait(ConfigureAwaitOptions.None);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasDiagnostic(code);
    }

    /// <summary>
    /// 验证对 Task&lt;T&gt; 调用 ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) 不会触发诊断。
    /// </summary>
    [Fact]
    public Task ConfigureAwaitOptions_ContinueOnCapturedContext_OnGenericTask_InGrainClass_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            var result = await Task.FromResult(42).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyHasNoDiagnostic(code);
    }

    #endregion

    #region Code Fix Tests

    /// <summary>
    /// 验证代码修复将 ConfigureAwait(false) 转换为 ConfigureAwait(true)。
    /// </summary>
    [Fact]
    public Task CodeFix_ConfigureAwaitFalse_ChangesToTrue()
    {
        var originalCode = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var expectedFixedCode = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(true);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyCodeFix(originalCode, expectedFixedCode);
    }

    /// <summary>
    /// 验证代码修复将 ValueTask 上的 ConfigureAwait(false) 转换为 ConfigureAwait(true)。
    /// </summary>
    [Fact]
    public Task CodeFix_ConfigureAwaitFalse_OnValueTask_ChangesToTrue()
    {
        var originalCode = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await GetValueAsync().ConfigureAwait(false);
                        }

                        private ValueTask GetValueAsync() => ValueTask.CompletedTask;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var expectedFixedCode = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await GetValueAsync().ConfigureAwait(true);
                        }

                        private ValueTask GetValueAsync() => ValueTask.CompletedTask;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyCodeFix(originalCode, expectedFixedCode);
    }

    /// <summary>
    /// 验证代码修复将 ConfigureAwait(ConfigureAwaitOptions.None) 转换为 ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)。
    /// </summary>
    [Fact]
    public Task CodeFix_ConfigureAwaitOptionsNone_ChangesToContinueOnCapturedContext()
    {
        var originalCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.None);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var expectedFixedCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyCodeFix(originalCode, expectedFixedCode);
    }

    /// <summary>
    /// 验证代码修复将 ContinueOnCapturedContext 添加至 ConfigureAwait(ConfigureAwaitOptions.ForceYielding)。
    /// </summary>
    [Fact]
    public Task CodeFix_ConfigureAwaitOptionsForceYielding_AddsContinueOnCapturedContext()
    {
        var originalCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var expectedFixedCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyCodeFix(originalCode, expectedFixedCode);
    }

    /// <summary>
    /// 验证代码修复将 ContinueOnCapturedContext 添加至组合的 ConfigureAwaitOptions。
    /// </summary>
    [Fact]
    public Task CodeFix_ConfigureAwaitOptionsCombined_AddsContinueOnCapturedContext()
    {
        var originalCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var expectedFixedCode = """
                    using System.Threading.Tasks;

                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifyCodeFix(originalCode, expectedFixedCode);
    }

    #endregion

    [Fact]
    public async Task NonConstantConfigureAwaitOptionsVariable_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions suppliedOptions)
                        {
                            var options = suppliedOptions;
                            await Task.Delay(100).ConfigureAwait(options);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions suppliedOptions);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ConfigureAwaitOptionsInvocationWithUnknownResult_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(int value)
                        {
                            await Task.Delay(100).ConfigureAwait(GetOptions(value));
                        }

                        private static ConfigureAwaitOptions GetOptions(int value) => (ConfigureAwaitOptions)value;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(int value);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CastOfUnknownConfigureAwaitOptions_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(int value)
                        {
                            await Task.Delay(100).ConfigureAwait((ConfigureAwaitOptions)value);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(int value);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ParenthesizedUnknownConfigureAwaitOptions_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options)
                        {
                            await Task.Delay(100).ConfigureAwait((options));
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BitwiseConfigureAwaitOptionsWithUnknownOperand_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options)
                        {
                            await Task.Delay(100).ConfigureAwait(options | ConfigureAwaitOptions.ForceYielding);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BitwiseConfigureAwaitOptionsWithUnknownRightOperand_DoesNotReportDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options, int value)
                        {
                            await Task.Delay(100).ConfigureAwait(
                                (options & ConfigureAwaitOptions.ForceYielding) | GetOptions(value));
                        }

                        private static ConfigureAwaitOptions GetOptions(int value) => (ConfigureAwaitOptions)value;
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options, int value);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public Task ConstNonCapturingConfigureAwaitOptions_ReportsExactDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            const ConfigureAwaitOptions options = ConfigureAwaitOptions.SuppressThrowing;
                            await Task.Delay(100).ConfigureAwait(options);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(code);
    }

    [Fact]
    public Task CastNonCapturingConfigureAwaitOptions_ReportsExactDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait((ConfigureAwaitOptions)6);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(code);
    }

    [Fact]
    public Task ParenthesizedNonCapturingConfigureAwaitOptions_ReportsExactDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(((ConfigureAwaitOptions.ForceYielding)));
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(code);
    }

    [Fact]
    public Task BitwiseNonCapturingConfigureAwaitOptionsExpression_ReportsExactDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething()
                        {
                            await Task.Delay(100).ConfigureAwait(
                                ((ConfigureAwaitOptions)4) | (ConfigureAwaitOptions)(1 << 1));
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(code);
    }

    [Fact]
    public Task NonConstantBitwiseAndNonCapturingConfigureAwaitOptionsExpression_ReportsExactDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options)
                        {
                            await Task.Delay(100).ConfigureAwait(options & ConfigureAwaitOptions.ForceYielding);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options);
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(code);
    }

    private async Task VerifySingleConfigureAwaitDiagnostic(string code, string? expectedInvocation = null)
    {
        var (diagnostics, completeSource) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ORLEANS0014", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.NotNull(diagnostic.Location.SourceTree);
        Assert.Equal(completeSource, diagnostic.Location.SourceTree.GetText().ToString());

        var syntaxTree = CSharpSyntaxTree.ParseText(completeSource);
        var invocation = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(candidate =>
                candidate.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.ValueText == "ConfigureAwait");

        expectedInvocation ??= invocation.ToString();
        Assert.Equal(expectedInvocation, invocation.ToString());
        Assert.Equal(invocation.Span, diagnostic.Location.SourceSpan);
        Assert.Equal(
            expectedInvocation,
            diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task NonTaskLikeConfigureAwaitMethod_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            new CustomAwaitable().ConfigureAwait(false);
                            return Task.CompletedTask;
                        }
                    }

                    public sealed class CustomAwaitable
                    {
                        public void ConfigureAwait(bool continueOnCapturedContext)
                        {
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ForeignConfigureAwaitOptionsOnTaskPrefixType_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    namespace Foreign
                    {
                        public enum ConfigureAwaitOptions
                        {
                            None = 0
                        }
                    }

                    namespace System.Threading.Tasks
                    {
                        public sealed class TaskPrefixAwaitable
                        {
                            public void ConfigureAwait(global::Foreign.ConfigureAwaitOptions options)
                            {
                            }
                        }
                    }

                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            new System.Threading.Tasks.TaskPrefixAwaitable().ConfigureAwait(
                                Foreign.ConfigureAwaitOptions.None);
                            return Task.CompletedTask;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonConstantBooleanConfigureAwaitArgument_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(bool continueOnCapturedContext)
                        {
                            await Task.CompletedTask.ConfigureAwait(continueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(bool continueOnCapturedContext);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RuntimeCompositeWithCaptureFlagOnRight_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options)
                        {
                            await Task.CompletedTask.ConfigureAwait(
                                (options & ConfigureAwaitOptions.ForceYielding) | ConfigureAwaitOptions.ContinueOnCapturedContext);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RuntimeCompositeWithCaptureFlagOnLeft_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(ConfigureAwaitOptions options)
                        {
                            await Task.CompletedTask.ConfigureAwait(
                                ConfigureAwaitOptions.ContinueOnCapturedContext | (options & ConfigureAwaitOptions.ForceYielding));
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(ConfigureAwaitOptions options);
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public Task RuntimeConditionalWithoutCaptureFlag_ShouldTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public async Task DoSomething(bool condition)
                        {
                            await Task.CompletedTask.ConfigureAwait(condition ? ConfigureAwaitOptions.ForceYielding : ConfigureAwaitOptions.SuppressThrowing);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething(bool condition);
                    }
                    """;

        return VerifySingleConfigureAwaitDiagnostic(
            code,
            "Task.CompletedTask.ConfigureAwait(condition ? ConfigureAwaitOptions.ForceYielding : ConfigureAwaitOptions.SuppressThrowing)");
    }

    [Fact]
    public async Task DynamicReceiverConfigureAwait_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            dynamic receiver = Task.CompletedTask;
                            receiver.ConfigureAwait(false);
                            return Task.CompletedTask;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DelegateValuedConfigureAwaitProperty_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    public sealed class ConfigureAwaitHolder
                    {
                        public Func<bool, Task> ConfigureAwait { get; } =
                            continueOnCapturedContext => Task.CompletedTask;
                    }

                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            var holder = new ConfigureAwaitHolder();
                            return holder.ConfigureAwait(false);
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TaskPrefixConfigureAwaitWithoutArguments_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    namespace System.Threading.Tasks
                    {
                        public sealed class TaskPrefixParameterlessAwaitable
                        {
                            public void ConfigureAwait()
                            {
                            }
                        }
                    }

                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            new System.Threading.Tasks.TaskPrefixParameterlessAwaitable().ConfigureAwait();
                            return Task.CompletedTask;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TaskPrefixConfigureAwaitWithNullArgument_ShouldNotTriggerDiagnostic()
    {
        var code = """
                    namespace System.Threading.Tasks
                    {
                        public sealed class TaskPrefixNullableAwaitable
                        {
                            public void ConfigureAwait(object? value)
                            {
                            }
                        }
                    }

                    public class MyGrain : Grain, IMyGrain
                    {
                        public Task DoSomething()
                        {
                            new System.Threading.Tasks.TaskPrefixNullableAwaitable().ConfigureAwait(null);
                            return Task.CompletedTask;
                        }
                    }

                    public interface IMyGrain : IGrainWithGuidKey
                    {
                        Task DoSomething();
                    }
                    """;

        var (diagnostics, _) = await GetDiagnosticsAsync(code, Array.Empty<string>());

        Assert.Empty(diagnostics);
    }
}
