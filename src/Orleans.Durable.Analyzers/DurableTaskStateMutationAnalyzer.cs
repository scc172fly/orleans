using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 长期任务机构的报告代码,更改 grain 国家所维持的物体,例如
/// <c>_order.Value.Lines.Add(line)</c> 或 <c>_state.State.Count++</c>,而不是通过
/// 状态 API。
/// </summary>
/// <remarks>
/// 分析遵循的是被改变的成员或接收者在收藏中变异到
/// grain 国家直接或通过当地人持有,而该地方人的申报与某一申报并用申报初始化,而且没有代码指定
/// 。价值类型和不可更改的记录无法通过该链条改变,不可更改的收藏回报
/// 修改副本,因此没有报告。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskStateMutationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DurableTaskDiagnostics.StateChangedInPlace);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is { } symbols)
            {
                start.RegisterOperationAction(
                    operation => Analyze(operation, symbols),
                    OperationKind.SimpleAssignment,
                    OperationKind.CompoundAssignment,
                    OperationKind.CoalesceAssignment,
                    OperationKind.Increment,
                    OperationKind.Decrement,
                    OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var holder = context.Operation switch
        {
            IInvocationOperation invocation when DurableTaskSymbols.IsCollectionMutation(invocation.TargetMethod) => invocation.Instance,
            IAssignmentOperation assignment => GetInstance(assignment.Target),
            IIncrementOrDecrementOperation increment => GetInstance(increment.Target),
            _ => null,
        };
        if (holder is not null
            && GetStateValue(holder, symbols) is { } value
            && symbols.IsInDurableBody(context.Operation, context.ContainingSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DurableTaskDiagnostics.StateChangedInPlace,
                context.Operation.Syntax.GetLocation(),
                value.Syntax.ToString()));
        }
    }

    /// <summary>返回其成员或元素的指定目标更改的对象。</summary>
    private static IOperation? GetInstance(IOperation target) => target switch
    {
        IMemberReferenceOperation member => member.Instance,
        IArrayElementReferenceOperation element => element.ArrayReference,
        _ => null,
    };

    /// <summary>返回 grain 状态值,该数值持有对象、成员之后的物体、元素和当地化名。</summary>
    private static IOperation? GetStateValue(IOperation holder, DurableTaskSymbols symbols)
    {
        for (var current = DurableTaskSymbols.Unwrap(holder); ; current = DurableTaskSymbols.Unwrap(current))
        {
            if (symbols.IsStateValue(current))
            {
                return current;
            }

            switch (current)
            {
                case IMemberReferenceOperation { Instance: { } instance }:
                    current = instance;
                    break;
                case IArrayElementReferenceOperation element:
                    current = element.ArrayReference;
                    break;
                case ILocalReferenceOperation local when GetAliasedStateValue(local, symbols) is { } aliased:
                    return aliased;
                default:
                    return null;
            }
        }
    }

    /// <summary>返回当当地人声明是其唯一任务时持有的 grain 状态值。</summary>
    private static IOperation? GetAliasedStateValue(ILocalReferenceOperation reference, DurableTaskSymbols symbols) =>
        reference.SemanticModel is { } semanticModel
        && DurableTaskSymbols.GetOnlyValue(reference.Local, semanticModel) is { } value
        && symbols.IsStateValue(DurableTaskSymbols.Unwrap(value))
            ? DurableTaskSymbols.Unwrap(value)
            : null;
}
