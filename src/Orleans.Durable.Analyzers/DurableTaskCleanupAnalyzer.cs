using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 报告代码,仅在任务取消和传过后才运行 <c>DurableTaskContext.CancellationToken</c>, 其中
/// 任务取消已取消, 以拨打 。
/// </summary>
/// <remarks>
/// 代码在任务取消后,当它是一个 <see langword="catch"/> 条款,或
/// a <see langword="if"/> 语句或有条件表达式,其过滤器或条件为
/// <c>DurableTaskContext.IsTaskCancellationRequested</c> 或与其作为操作性连接。任务取消从不
/// 区域会被取消。 雀巢羊羔和本地功能可以在另一个任务的背景中运行, 所以它们
/// 未经检查
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

    /// <summary>返回是否只有 <paramref name="requested"/> 是条件才为真实。</summary>
    private static bool Requires(IOperation condition, IPropertySymbol requested) => DurableTaskSymbols.Unwrap(condition) switch
    {
        IPropertyReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Property, requested),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction =>
            Requires(conjunction.LeftOperand, requested) || Requires(conjunction.RightOperand, requested),
        _ => false,
    };
}
