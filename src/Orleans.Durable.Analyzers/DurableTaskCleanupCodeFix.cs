using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.Durable.Analyzers;

/// <summary>在任务取消后的清理代码中将 <c>CancellationToken</c> 替换为 <c>AttemptCancellationToken</c>，并保留其接收对象。</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DurableTaskCleanupCodeFix)), Shared]
public sealed class DurableTaskCleanupCodeFix : CodeFixProvider
{
    private const string Title = "Pass AttemptCancellationToken";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.CanceledTokenInCleanup.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        // C# 文档始终具有语法根节点。
        var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var name = root.FindNode(context.Span, getInnermostNodeForTie: true) switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (name is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(
                    name,
                    SyntaxFactory.IdentifierName("AttemptCancellationToken").WithTriviaFrom(name)))),
                equivalenceKey: Title),
            context.Diagnostics);
    }
}
