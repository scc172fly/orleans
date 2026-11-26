using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.Tests;

/// <summary>
/// https://github.com/dotnet/orleans/issues/9290的回归测试:代码生成器必须逃跑
/// 组装名称是 C# 保留识别符号, 当它们用于构建生成的元数据命名空间时 。
/// </summary>
public class ReservedIdentifierNamespaceTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("class")]
    [InlineData("namespace")]
    public async Task AssemblyNamedAfterReservedKeywordProducesValidGeneratedNamespace(string assemblyName)
    {
        const string code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public sealed class Data
            {
                [Id(0)]
                public int Value { get; set; }
            }
            """;

        var cancellationToken = TestContext.Current.CancellationToken;
        var compilation = await TestCompilationHelper.CreateCompilation(code, assemblyName);
        AssertNoErrors(compilation.GetDiagnostics(cancellationToken));

        var generator = new OrleansSerializationSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            driverOptions: new GeneratorDriverOptions(default));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics, cancellationToken);
        AssertNoErrors(generatorDiagnostics);

        // 生成的元数据源必须编译, 且不出现语法错误 i.e. 命名空间段
        // 源自保留关键词组装名称的源代码必须逃跑(e.g. "OrleansCodeGen.@default")。
        AssertNoErrors(outputCompilation.GetDiagnostics(cancellationToken));

        var metadataSource = driver.GetRunResult().Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName.Contains(".orleans.metadata.g.cs", StringComparison.Ordinal));

        var namespaceDeclaration = CSharpSyntaxTree.ParseText(metadataSource.SourceText.ToString().TrimStart('﻿'), cancellationToken: cancellationToken)
            .GetCompilationUnitRoot(cancellationToken)
            .DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Single();

        Assert.Equal($"OrleansCodeGen.@{assemblyName}", namespaceDeclaration.Name.ToString());
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }
}
