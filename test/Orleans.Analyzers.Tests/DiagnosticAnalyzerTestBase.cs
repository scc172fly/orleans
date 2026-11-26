// 取自实体框架核心分析器测试
// https://github.com/aspnet/EntityFrameworkCore/blob/cbefe76162b16c1122629dbf6c85becbbb5cdc8e/test/EFCore.Analyzers.Tests/TestUtilities/DiagnosticAnalyzerTestBase.cs
// Copyright (c) .NET Foundation. All rights reserved.
// 许可证根据阿帕奇牌照,2.0版。见项目根 License.txt ,关于许可证信息。

using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using Xunit;

namespace Analyzers.Tests
{
    /// <summary>
    /// 在 Orleans 项目中测试Roslyn诊断分析器的基础班。
    /// 提供共同的基础设施,用于编制测试代码和运行分析器,以核实诊断。
    /// 这使能够测试 Orleans- 有助于开发者避免常见错误的具体代码分析规则。
    /// </summary>
    /// <typeparam name="TDiagnosticAnalyzer">正在测试的诊断分析器类型。</typeparam>
    public abstract class DiagnosticAnalyzerTestBase<TDiagnosticAnalyzer>
        where TDiagnosticAnalyzer : DiagnosticAnalyzer, new()
    {
        private static readonly string[] Usings = new[] {
            "System",
            "System.Threading.Tasks",
            "Orleans"
        };

        /// <summary>
        /// 为所有 Orleans grain 接口类型提供测试数据。
        /// 用于理论测试以确保分析者正确使用所有 grain 接口变量。
        /// </summary>
        public static IEnumerable<object[]> GrainInterfaces =>
            new List<object[]>
            {
                new object[] { "Orleans.IGrain" },
                new object[] { "Orleans.IGrainWithStringKey" },
                new object[] { "Orleans.IGrainWithGuidKey" },
                new object[] { "Orleans.IGrainWithGuidCompoundKey" },
                new object[] { "Orleans.IGrainWithIntegerKey" },
                new object[] { "Orleans.IGrainWithIntegerCompoundKey" }
            };

        /// <summary>
        /// 创建一个正在测试的诊断分析器实例 。
        /// 可以在衍生的分类中被取代, 以定制分析器创建。
        /// </summary>
        protected virtual DiagnosticAnalyzer CreateDiagnosticAnalyzer() => new TDiagnosticAnalyzer();

        /// <summary>
        /// 断言提供源代码在分析时不会产生诊断结果。
        /// 用来验证有效的代码模式不会引发假阳性
        /// </summary>
        /// <param name="source">C# 源代码要分析。</param>
        /// <param name="extraUsings">补充使用语句以包括。</param>
        protected async Task AssertNoDiagnostics(string source, params string[] extraUsings)
        {
            var (diagnostics, _) = await this.GetDiagnosticsAsync(source, extraUsings);
            Assert.Empty(diagnostics);
        }

        /// <summary>
        /// 汇编所提供的源代码,并运行对它进行诊断分析的功能。
        /// 返回与格式化源代码一起生成的任何诊断。
        /// </summary>
        /// <param name="source">C# 源代码要分析。</param>
        /// <param name="extraUsings">补充使用语句以包括。</param>
        /// <returns>含有诊断阵列和格式化源代码的图例。</returns>
        protected virtual async Task<(Diagnostic[], string)> GetDiagnosticsAsync(string source, params string[] extraUsings)
        {
            var sb = new StringBuilder();
            foreach (var @using in Usings.Concat(extraUsings))
            {
                sb.AppendLine($"using {@using};");
            }
            sb.AppendLine(source);

            var sourceText = sb.ToString();
            return (await this.GetDiagnosticsFullSourceAsync(sourceText), sourceText);
        }

        protected async Task<Diagnostic[]> GetDiagnosticsFullSourceAsync(string source)
        {
            // C##项目总是支持汇编。
            var compilation = (await CreateProject(source).GetCompilationAsync())!;
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

            Assert.Empty(errors);
            var analyzer = this.CreateDiagnosticAnalyzer();
            var compilationWithAnalyzers
                = compilation
                    .WithOptions(
                        compilation.Options.WithSpecificDiagnosticOptions(
                            analyzer.SupportedDiagnostics.ToDictionary(d => d.Id, d => ReportDiagnostic.Default)))
                    .WithAnalyzers(ImmutableArray.Create(analyzer));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();
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
    }
}
