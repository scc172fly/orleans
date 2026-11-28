using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>报告未命名的持久化操作，这些操作的自动生成标识可能在各次运行及部署之间发生变化。</summary>
/// <remarks>
/// 持久化任务体的未命名操作使用执行实例的下一个生成标识符：子任务、效果或
/// 分离启动使用 <c>$child-n</c>，而被等待的组合器使用诸如
/// <c>$when-all-n</c> 的操作标识。每种标识都统计在此之前执行的同类未命名操作，因此位于循环、
/// 条件中或位于另一个同类未命名操作之后的操作，可能获得不同的标识。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskIdentityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>标记可通过 <c>WithId</c> 命名的操作的诊断属性。</summary>
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
        /// <summary>子任务、效果和分离启动使用 <c>$child-n</c>。</summary>
        Child,

        /// <summary>被等待的组合器，使用诸如 <c>$when-all-n</c> 的操作标识。</summary>
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
    /// 返回某个操作启动的未命名操作：等待、调度或投递的、未使用
    /// <c>WithId</c> 的定义（诊断会标记该定义），或未命名的 <c>DurableTask.RunInline</c> 效果。
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
    /// 返回为操作分配标识的持久化任务体，并指示二者之间的代码（例如查询所执行的
    /// Lambda 表达式或局部函数）是否可能多次运行该操作。
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

    /// <summary>返回节点是否在其所属任务体的循环或条件中运行。</summary>
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
