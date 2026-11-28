using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.Tests;

/// <summary>
/// https://github.com/dotnet/orleans/issues/9290 的回归测试：如果程序集名称是 C# 保留标识符，代码生成器在
/// 使用这些名称构建生成的元数据命名空间时必须对其转义。
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

        // 生成的元数据源码必须能够编译且无语法错误，即源于保留关键字程序集名称的命名空间段
        // 必须经过转义（例如 "OrleansCodeGen.@default"）。
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
