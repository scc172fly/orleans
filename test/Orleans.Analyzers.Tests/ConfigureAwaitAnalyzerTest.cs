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
/// 警告分析师的测试 ConfigureAwait(虚假)或 ConfigureAwait 无
/// ContinueOnCapturedContext 内 grain 代码。 Grains 必须保持同步的上下文
/// 以确保在 grain启动背景下适当执行。
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

        // 使用
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

        // 创建项目并获得诊断
        var project = CreateProject(fullOriginalCode);
        var document = project.Documents.First();
        // C##项目总是支持汇编。
        var compilation = (await project.GetCompilationAsync())!;

        var analyzer = new ConfigureAwaitAnalyzer();
        var compilationWithAnalyzers = compilation
            .WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                analyzer.SupportedDiagnostics.ToDictionary(d => d.Id, d => ReportDiagnostic.Default)))
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.NotEmpty(diagnostics);

        // 应用代码修正
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
        // 应用代码修正保存原始文件。
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

        // System.Private.CoreLib 从文件支持的组装装入 。
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

        // 该项目是上文提到的这一解决办法中增加的。
        return solution.GetProject(projectId)!
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #region ConfigureAwait(false) in Grain

    /// <summary>
    /// 证明: ConfigureAwait(假)a grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(假)a grain 执行可重设接口的分类触发诊断。
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
    /// 证明: ConfigureAwait在一个POCO中(假) grain 课级执行 IGrainBase 触发诊断。
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
    /// 证明: ConfigureAwait(假)a grain 类类的 GrainType 属性触发诊断。
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
    /// 证明: ConfigureAwait(假)通用 grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(真)在 grain 类不会触发诊断。
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
    /// 证明在普通阶级(无继承)中的 ConfigureAwait(假冒)不引发诊断。
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
    /// 证明: ConfigureAwait在一个班级中,grain 接口不触发诊断。
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
    /// 证明: ConfigureAwait在一个阶层中,他继承了一个非grain 基类不触发诊断。
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
    /// 证明: ConfigureAwait在一个阶层中,在深非grain 继承不会引发诊断。
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
    /// 证明支架 ConfigureAwait(假)不触发诊断。
    /// 条理不能为 grains。
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
    /// 证明记录中的 ConfigureAwait(假)不触发诊断。
    /// 记录不能是 grains (它们不继承 Grain 或执行 IGrainBase)。
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
    /// 证明在创纪录的支架中 ConfigureAwait(假)不触发诊断。
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
    /// 证明: ConfigureAwait在一个执行不可(不) grain ) 并不触发诊断。
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
    /// 证明静态类中的 ConfigureAwait(假)不触发诊断。
    /// 静态类不能为 grains。
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
    /// 证明: ConfigureAwait抽象的(假)grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait(假)一般非-grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait在一个非非-grain 类不会触发诊断。
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
    /// 证明 ConfigureAwait(ConfigureAwaitOptions)没有 ContinueOnCapturedContext 
    /// 非grain 类中,不触发诊断。
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
    /// 证明没有 ConfigureAwait a 中 grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait在一个班级执行(假) IGrainBase 触发诊断。
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
    /// 证明: ConfigureAwait在一个班级执行(假) ISystemTarget 触发诊断。
    /// ISystemTarget 中 Orleans 命名空间( 不 Orleans.Runtime))定义的定义 Orleans.Core.Abstractions.
    /// </summary>
    [Fact]
    public Task ConfigureAwaitFalse_InISystemTargetImplementation_ShouldTriggerDiagnostic()
    {
        // 注: ISystemTarget 的定义在名称空间 Orleans (在 Orleans.Core.Abstractions 组装中)中,
        // 因此不需要额外使用,因为我们已经“使用 Orleans;”
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
    /// 证明: ConfigureAwait在一个山里,在一只羊中, grain 等级触发诊断。
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
    /// 证明 ConfigureAwait(假)类中嵌套的类别在 grain 类中触发诊断。
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

        // 嵌套类位于 grain 类内, 所以仍然应该触发
        return VerifyHasDiagnostic(code);
    }

    #endregion

    #region Inherited grain classes

    /// <summary>
    /// 证明: ConfigureAwait在一个从他人继承的阶级中, grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(假) ValueTask a 中 grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(假) ValueTask&lt;T&gt; a 中 grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(真) 开 ValueTask a 中 grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait(假) ValueTask 中为grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait(假) IAsyncEnumerable 等待在 grain 等级触发诊断。
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
    /// 证明: ConfigureAwait(真) 开 IAsyncEnumerable 等待在 grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait(假) IAsyncEnumerable 中为grain 类不会触发诊断。
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
    /// 证明等待的每个人 ConfigureAwait a 中 grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait任务( 虚假)&lt;T&gt; a 中 grain 等级触发诊断。
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
    /// 证明: ConfigureAwait任务( rit)&lt;T&gt; a 中 grain 类不会触发诊断。
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
    /// 证明 ConfigureAwait(ConfigureAwaitOptions.None)类中的 grain 类别触发诊断
    /// 因为它不包括 ContinueOnCapturedContext。
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
    /// 证明 ConfigureAwait(ConfigureAwaitOptions.ForceYielding)类中的 grain 类别触发诊断
    /// 因为它不包括 ContinueOnCapturedContext。
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
    /// 证明 ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)类中的 grain 类别触发诊断
    /// 因为它不包括 ContinueOnCapturedContext。
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
    /// 证明 ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)类中的 grain 类别
    /// 并不触发诊断。
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
    /// 证明 ConfigureAwait 加混合旗,包括 ContinueOnCapturedContext
    /// 并不触发诊断。
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
    /// 证明 ConfigureAwait 加加加号,但不包括 ContinueOnCapturedContext
    /// 触发诊断。
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
    /// 证明: ConfigureAwait(ConfigureAwaitOptions)在非-grain 类不会触发诊断。
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
    /// 证明: ConfigureAwait(ConfigureAwaitOptions任务&lt;T&gt; 工作正确 。
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
    /// 证明: ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext任务&lt;T&gt; 并不触发诊断。
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
    /// 证明代码修正将 ConfigureAwait(假)转换为 ConfigureAwait(真)。
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
    /// 验证代码修正转换 ConfigureAwait(假)至 ConfigureAwait(真) 开 ValueTask.
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
    /// 验证代码修正转换 ConfigureAwait(ConfigureAwaitOptions.None至 ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext).
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
    /// 证明代码固定将 ContinueOnCapturedContext 添加到 ConfigureAwait(ConfigureAwaitOptions.ForceYielding)。
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
    /// 证明代码修正将 ContinueOnCapturedContext 添加到合并 ConfigureAwaitOptions。
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
