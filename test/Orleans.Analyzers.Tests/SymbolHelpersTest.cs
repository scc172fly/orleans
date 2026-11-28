using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Orleans.Analyzers;
using Xunit;

namespace Analyzers.Tests;

[TestCategory("BVT"), TestCategory("Analyzer")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Analyzer")]
public class SymbolHelpersTest
{
    [Fact]
    public void HasAttribute_MetadataSymbols_ShouldMatchWithoutApplicationSyntax()
    {
        const string source = """
            public sealed class MarkerAttribute : System.Attribute { }
            public sealed class OtherAttribute : System.Attribute { }

            [Marker]
            public class Attributed
            {
                [Marker] public void Method() { }
            }

            public class Unattributed { }
            """;
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
        ];
        var definition = CSharpCompilation.Create(
            "AttributeDefinitions",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = definition.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var consumer = CSharpCompilation.Create(
            "Consumer",
            references: [.. references, MetadataReference.CreateFromImage(stream.ToArray())],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var marker = consumer.GetTypeByMetadataName("MarkerAttribute")!;
        var other = consumer.GetTypeByMetadataName("OtherAttribute")!;
        var type = consumer.GetTypeByMetadataName("Attributed")!;
        var method = Assert.Single(type.GetMembers("Method"));
        var unattributed = consumer.GetTypeByMetadataName("Unattributed")!;

        // 辅助方法为内部成员。在不新增友元 API 的前提下，仅验证判断是否存在的重载。
        // 元数据特性不具有语法：此处如果委托给获取位置的重载，将抛出异常。
        var helperType = typeof(GenerateAliasAttributesAnalyzer).Assembly.GetType("Orleans.Analyzers.SymbolHelpers", throwOnError: true)!;
        var helperMethod = helperType.GetMethod(
            "HasAttribute",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(ISymbol), typeof(INamedTypeSymbol)],
            modifiers: null)!;
        var hasAttribute = helperMethod.CreateDelegate<Func<ISymbol, INamedTypeSymbol, bool>>();

        Assert.Empty(type.DeclaringSyntaxReferences);
        Assert.Null(Assert.Single(type.GetAttributes()).ApplicationSyntaxReference);
        Assert.Null(Assert.Single(method.GetAttributes()).ApplicationSyntaxReference);
        Assert.True(hasAttribute(type, marker));
        Assert.True(hasAttribute(method, marker));
        Assert.False(hasAttribute(type, other));
        Assert.False(hasAttribute(method, other));
        Assert.False(hasAttribute(unattributed, marker));
    }
}
