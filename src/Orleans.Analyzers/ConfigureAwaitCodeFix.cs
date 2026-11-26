using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Orleans.Analyzers;

/// <summary>
/// 将 ConfigureAwait(假) 转换为 ConfigureAwait(ride) 和
/// 将 ContinueOnCapturedContext 添加到 ConfigureAwait(ConfigureAwaitOptions)调用。
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConfigureAwaitCodeFix)), Shared]
public class ConfigureAwaitCodeFix : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ConfigureAwaitAnalyzer.RuleId);
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // 查找诊断中发现的援引表达式
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null)
        {
            return;
        }

        // 获取语义模型以确定要应用的固定
        var semanticModel = (await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // 检查参数类型以确定要应用的固定
        if (methodSymbol.Parameters.Length == 1)
        {
            var parameterType = methodSymbol.Parameters[0].Type;

            if (parameterType.SpecialType == SpecialType.System_Boolean)
            {
                // ConfigureAwait(bool) 的固定 - 假更改为真
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Resources.ConfigureAwaitCodeFixTitle,
                        createChangedDocument: ct => FixConfigureAwaitBoolAsync(context.Document, invocation, ct),
                        equivalenceKey: ConfigureAwaitAnalyzer.RuleId + "_Bool"),
                    diagnostic);
            }
            else if (string.Equals(parameterType.ToDisplayString(), "System.Threading.Tasks.ConfigureAwaitOptions", StringComparison.Ordinal))
            {
                // 固定 ConfigureAwait(ConfigureAwaitOptions- 加 - 加 ContinueOnCapturedContext 国旗
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Resources.ConfigureAwaitCodeFixTitle,
                        createChangedDocument: ct => FixConfigureAwaitOptionsAsync(context.Document, invocation, semanticModel, ct),
                        equivalenceKey: ConfigureAwaitAnalyzer.RuleId + "_Options"),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> FixConfigureAwaitBoolAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;

        // 以“ true” 而不是“ false” 创建新参数
        var newArgument = Argument(LiteralExpression(SyntaxKind.TrueLiteralExpression));
        var newArgumentList = ArgumentList(SingletonSeparatedList(newArgument));

        // 替换引号列表
        var newInvocation = invocation.WithArgumentList(newArgumentList);
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> FixConfigureAwaitOptionsAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;

        var arguments = invocation.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count == 0)
        {
            return document;
        }

        var existingArgument = arguments.Value[0].Expression;

        // 检查现有参数是否 ConfigureAwaitOptions.None
        var constantValue = semanticModel.GetConstantValue(existingArgument, cancellationToken);
        ExpressionSyntax newExpression;

        if (constantValue.HasValue && constantValue.Value is int intValue && intValue == 0)
        {
            // 如果为零(0),请替换为 ContinueOnCapturedContext
            newExpression = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("ConfigureAwaitOptions"),
                IdentifierName("ContinueOnCapturedContext"));
        }
        else
        {
            // 否则,加上 ContinueOnCapturedContext 使用比特 OR
            var continueOnCapturedContext = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("ConfigureAwaitOptions"),
                IdentifierName("ContinueOnCapturedContext"));

            newExpression = BinaryExpression(
                SyntaxKind.BitwiseOrExpression,
                existingArgument.WithoutTrivia(),
                continueOnCapturedContext);
        }

        var newArgument = Argument(newExpression);
        var newArgumentList = ArgumentList(SingletonSeparatedList(newArgument));

        var newInvocation = invocation.WithArgumentList(newArgumentList);
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }
}
