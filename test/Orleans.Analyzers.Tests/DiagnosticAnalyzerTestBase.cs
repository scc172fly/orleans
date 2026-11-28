// Derived from Entity Framework Core Analyzer Tests
// https://github.com/aspnet/EntityFrameworkCore/blob/cbefe76162b16c1122629dbf6c85becbbb5cdc8e/test/EFCore.Analyzers.Tests/TestUtilities/DiagnosticAnalyzerTestBase.cs
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
    /// Orleans 项目中测试 Roslyn 诊断分析器的基类。
    /// 提供用于编译测试代码并运行分析器以验证诊断的通用基础设施。
    /// 借此可以测试 Orleans 特有的代码分析规则，帮助开发者避免常见错误。
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
        /// 提供涵盖所有 Orleans Grain 接口类型的测试数据。
        /// 理论测试使用它来确保分析器能够正确处理所有 Grain 接口变体。
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
        /// 创建被测试的诊断分析器实例。
        /// 派生类可以重写此方法，自定义分析器的创建方式。
        /// </summary>
        protected virtual DiagnosticAnalyzer CreateDiagnosticAnalyzer() => new TDiagnosticAnalyzer();

        /// <summary>
        /// 断言所提供的源码经过分析后不会产生诊断。
        /// 用于验证有效的代码模式不会触发误报。
        /// </summary>
        /// <param name="source">要分析的 C# 源码。</param>
        /// <param name="extraUsings">需要包含的额外 using 指令。</param>
        protected async Task AssertNoDiagnostics(string source, params string[] extraUsings)
        {
            var (diagnostics, _) = await this.GetDiagnosticsAsync(source, extraUsings);
            Assert.Empty(diagnostics);
        }

        /// <summary>
        /// 编译所提供的源码，并对其运行诊断分析器。
        /// 返回产生的所有诊断以及格式化后的源码。
        /// </summary>
        /// <param name="source">要分析的 C# 源码。</param>
        /// <param name="extraUsings">需要包含的额外 using 指令。</param>
        /// <returns>包含诊断数组和格式化源码的元组。</returns>
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
            // C# 项目始终支持编译。
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
    }
}
