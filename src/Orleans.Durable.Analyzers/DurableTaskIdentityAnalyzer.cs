using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>报告未命名的持久行动,其产生的识别资料可在运行和部署之间转移。</summary>
/// <remarks>
/// 持久任务机构无名操作,则采用处决后下一个身份识别特征:儿童、效果或
/// 独立启动时使用 <c>$child-n</c>,等待的组合器使用操作识别符,例如
/// <c>$when-all-n</c>。每个单位都计及其面前的无名型号操作,因此一个循环或一个循环中操作
/// 条件,或是在另一种未命名的此类操作之后,可再获得另一个识别特征。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskIdentityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>诊断特性是指 <c>WithId</c> 缩略语</summary>
    internal const string WithIdProperty = "WithId";

    private static readonly ImmutableDictionary<string, string?> WithIdProperties =
        ImmutableDictionary<string, string?>.Empty.Add(WithIdProperty, null);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.UnnamedOperation);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is { } symbols)
            {
                start.RegisterOperationBlockAction(block => Analyze(block, symbols));
            }
        });
    }

    private enum Counter
    {
        /// <summary>儿童、影响和分离的开始, <c>$child-n</c>.</summary>
        Child,

        /// <summary>等待的组合集集机,它们使用诸如 <c>$when-all-n</c>.</summary>
        Operation,
    }

    private static void Analyze(OperationBlockAnalysisContext context, DurableTaskSymbols symbols)
    {
        var sites = new List<(SyntaxNode Scope, Counter Counter, SyntaxNode Node, bool Nameable, bool Repeated)>();
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (GetUnnamedOperation(operation, symbols) is { } site && GetScope(operation, context.OwningSymbol, symbols) is { } scope)
                {
                    sites.Add((scope.Node, site.Counter, site.Node, site.Nameable, scope.Repeated));
                }
            }
        }

        foreach (var group in sites.GroupBy(static site => (site.Scope, site.Counter)))
        {
            var first = true;
            foreach (var site in group.OrderBy(static site => site.Node.Span.End))
            {
                if (!first || site.Repeated || IsConditionalOrRepeated(site.Node, site.Scope))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DurableTaskDiagnostics.UnnamedOperation,
                        site.Node.GetLocation(),
                        site.Nameable ? WithIdProperties : null));
                }

                first = false;
            }
        }
    }

    /// <summary>
    /// 返回一个操作开始的未命名操作:等待、计划或已张贴定义,不包含
    /// <c>WithId</c>,诊断标记或未命名 <c>DurableTask.RunInline</c> 效应。
    /// </summary>
    private static (Counter Counter, SyntaxNode Node, bool Nameable)? GetUnnamedOperation(IOperation operation, DurableTaskSymbols symbols)
    {
        IOperation started;
        var awaited = false;
        switch (operation)
        {
            case IAwaitOperation await when symbols.IsDefinitionType(DurableTaskSymbols.Unwrap(await.Operation).Type):
                started = await.Operation;
                awaited = true;
                break;
            case IInvocationOperation { TargetMethod.Name: "ScheduleAsync" or "Post" } start
                when DurableTaskSymbols.IsDurableTasksType(start.TargetMethod.ContainingType)
                && DurableTaskSymbols.GetExtensionReceiver(start) is { } receiver
                && symbols.IsDefinitionType(DurableTaskSymbols.Unwrap(receiver).Type):
                started = receiver;
                break;
            case IInvocationOperation effect when symbols.IsRunInline(effect)
                && !effect.TargetMethod.Parameters.Any(static parameter => parameter.Type.SpecialType == SpecialType.System_String):
                return (Counter.Child, effect.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } ? memberAccess.Name : effect.Syntax, false);
            default:
                return null;
        }

        if (DurableTaskSymbols.GetDefinition(started, "WithId", out var named) is not IInvocationOperation definition
            || named
            || DurableTaskSymbols.SuppliesRootId(started)
            || (awaited && symbols.IsSignalWait(definition)))
        {
            return null;
        }

        var counter = awaited && symbols.IsCombinator(definition) ? Counter.Operation : Counter.Child;
        return (counter, definition.Syntax, true);
    }

    /// <summary>
    /// 返回指定操作标识符的持久任务机构,以及两者之间是否代码,例如
    /// imbda , 查询运行或本地函数, 可以不止一次运行此操作 。
    /// </summary>
    private static (SyntaxNode Node, bool Repeated)? GetScope(IOperation operation, ISymbol owningSymbol, DurableTaskSymbols symbols)
    {
        var repeated = false;
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation function:
                    if (symbols.IsDurableBody(function.Symbol)
                        || (DurableTaskSymbols.GetDelegateReceiver(function) is { } receiver && symbols.IsRun(receiver)))
                    {
                        return (function.Syntax, repeated);
                    }

                    if (DurableTaskSymbols.GetDelegateReceiver(function) is { } effect && symbols.IsRunInline(effect))
                    {
                        return null;
                    }

                    repeated = true;
                    break;
                case ILocalFunctionOperation local:
                    if (symbols.IsDurableBody(local.Symbol))
                    {
                        return (local.Syntax, repeated);
                    }

                    repeated = true;
                    break;
                case { Parent: null } root when owningSymbol is IMethodSymbol method && symbols.IsDurableBody(method):
                    return (root.Syntax, repeated);
            }
        }

        return null;
    }

    /// <summary>返回一个节点是循环运行的,还是其体内的条件。</summary>
    private static bool IsConditionalOrRepeated(SyntaxNode node, SyntaxNode scope)
    {
        for (var child = node; child.Parent is { } parent && child != scope; child = parent)
        {
            var conditional = parent switch
            {
                IfStatementSyntax @if => child != @if.Condition,
                SwitchSectionSyntax or CatchClauseSyntax => true,
                SwitchExpressionArmSyntax arm => child != arm.Pattern,
                ConditionalExpressionSyntax conditionalExpression => child != conditionalExpression.Condition,
                BinaryExpressionSyntax binary => binary.Right == child
                    && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression,
                AssignmentExpressionSyntax assignment => assignment.Right == child && assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression),
                ConditionalAccessExpressionSyntax access => child == access.WhenNotNull,
                ForStatementSyntax @for => child != @for.Declaration && !(child is ExpressionSyntax expression && @for.Initializers.Contains(expression)),
                CommonForEachStatementSyntax forEach => child == forEach.Statement,
                WhileStatementSyntax or DoStatementSyntax => true,
                _ => false,
            };
            if (conditional)
            {
                return true;
            }
        }

        return false;
    }
}
