using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 使用 <c>WithId</c> 为未命名的持久化操作命名，名称源自定义该操作的方法，
/// 例如从 <c>ChargeAsync</c> 得到 <c>charge</c>，并确保名称与同一任务体中其他名称不同。
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DurableTaskIdentityCodeFix)), Shared]
public sealed class DurableTaskIdentityCodeFix : CodeFixProvider
{
    private const string Title = "Name the operation with WithId";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.UnnamedOperation.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(static async (context, document, diagnostics) =>
            await NameAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Properties.ContainsKey(DurableTaskIdentityAnalyzer.WithIdProperty))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        cancellationToken => NameAsync(context.Document, ImmutableArray.Create(diagnostic), cancellationToken),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>按源码顺序为每个被标记的定义分配其任务体尚未使用的名称。</summary>
    private static async Task<Document> NameAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        // C# 文档始终具有语法根节点。
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var usedNames = new Dictionary<SyntaxNode, HashSet<string>>();
        var names = new Dictionary<SyntaxNode, string>();
        foreach (var diagnostic in diagnostics.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start))
        {
            if (!diagnostic.Properties.ContainsKey(DurableTaskIdentityAnalyzer.WithIdProperty)
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax definition)
            {
                continue;
            }

            var body = definition.FirstAncestorOrSelf<SyntaxNode>(static node =>
                node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax)!;
            if (!usedNames.TryGetValue(body, out var used))
            {
                usedNames[body] = used = GetNames(body);
            }

            var baseName = GetBaseName(definition);
            var name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++)
            {
                name = $"{baseName}-{suffix}";
            }

            names[definition] = name;
        }

        return document.WithSyntaxRoot(root.ReplaceNodes(
            names.Keys,
            (original, rewritten) => SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        ((ExpressionSyntax)rewritten).WithoutTrivia(),
                        SyntaxFactory.IdentifierName("WithId")),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(names[original]))))))
                .WithTriviaFrom(rewritten)));
    }

    /// <summary>返回任务体传给 <c>WithId</c> 和 <c>DurableTask.RunInline</c> 的常量名称。</summary>
    private static HashSet<string> GetNames(SyntaxNode body)
    {
        var names = new HashSet<string>();
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WithId" or "RunInline" }
                && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                names.Add(literal.Token.ValueText);
            }
        }

        return names;
    }

    /// <summary>返回定义所调用方法的短横线分隔命名形式（kebab-case），去掉其 <c>Async</c> 后缀。</summary>
    private static string GetBaseName(InvocationExpressionSyntax definition)
    {
        var method = definition.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty,
        };
        if (method.Length > "Async".Length && method.EndsWith("Async", System.StringComparison.Ordinal))
        {
            method = method.Substring(0, method.Length - "Async".Length);
        }

        var builder = new StringBuilder(method.Length + 4);
        foreach (var character in method)
        {
            if (char.IsUpper(character) && builder.Length > 0)
            {
                builder.Append('-');
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length > 0 ? builder.ToString() : "operation";
    }
}
