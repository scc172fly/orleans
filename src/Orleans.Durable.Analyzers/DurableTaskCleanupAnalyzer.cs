using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 报告仅在任务取消后执行、却将 <c>DurableTaskContext.CancellationToken</c>（此令牌
/// 已因任务取消而取消）传入调用的代码。
/// </summary>
/// <remarks>
/// 当代码位于 <see langword="catch"/> 子句的处理程序中，或位于
/// <see langword="if"/> 语句或条件表达式的为真分支中，且其筛选器或条件为
/// <c>DurableTaskContext.IsTaskCancellationRequested</c> 或以其为操作数的逻辑与表达式时，该代码仅在任务取消后执行。任务取消不可
/// 撤销，因此此区域始终处于已取消状态。嵌套 Lambda 表达式和局部函数可能在其他任务的上下文中运行，因此
/// 不检查它们。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskCleanupAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.CanceledTokenInCleanup);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is { IsTaskCancellationRequested: { } requested, ContextCancellationToken: { } token })
            {
                start.RegisterOperationAction(operation => AnalyzeArgument(operation, requested, token), OperationKind.Argument);
            }
        });
    }

    private static void AnalyzeArgument(OperationAnalysisContext context, IPropertySymbol requested, IPropertySymbol token)
    {
        if (DurableTaskSymbols.Unwrap(((IArgumentOperation)context.Operation).Value) is IPropertyReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Property, token)
            && RunsOnlyAfterTaskCancellation(reference, requested))
        {
            context.ReportDiagnostic(Diagnostic.Create(DurableTaskDiagnostics.CanceledTokenInCleanup, reference.Syntax.GetLocation()));
        }
    }

    private static bool RunsOnlyAfterTaskCancellation(IOperation operation, IPropertySymbol requested)
    {
        for (var current = operation; current.Parent is { } parent; current = parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    return false;
                case ICatchClauseOperation clause when clause.Handler == current && clause.Filter is { } filter && Requires(filter, requested):
                case IConditionalOperation conditional when conditional.WhenTrue == current && Requires(conditional.Condition, requested):
                    return true;
            }
        }

        return false;
    }

    /// <summary>返回该条件是否仅在 <paramref name="requested"/> 为真时才为真。</summary>
    private static bool Requires(IOperation condition, IPropertySymbol requested) => DurableTaskSymbols.Unwrap(condition) switch
    {
        IPropertyReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Property, requested),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction =>
            Requires(conjunction.LeftOperand, requested) || Requires(conjunction.RightOperand, requested),
        _ => false,
    };
}
