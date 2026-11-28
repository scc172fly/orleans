using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 报告持久化任务体中就地修改 Grain 状态所持对象的代码，例如
/// <c>_order.Value.Lines.Add(line)</c> 或 <c>_state.State.Count++</c>，而非通过状态
/// API 赋予新值的代码。
/// </summary>
/// <remarks>
/// 分析沿着被修改成员或集合修改接收对象的实例链，查找到 Grain 状态所持的值：
/// 可以直接到达该值，也可以通过在声明时用该值初始化、且未被任何代码再次赋值的
/// 局部变量到达。值类型和不可变 record 无法通过此链修改，而不可变集合返回
/// 修改后的副本，因此两者都不会被报告。
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

    /// <summary>返回赋值目标所修改成员或元素所属的对象。</summary>
    private static IOperation? GetInstance(IOperation target) => target switch
    {
        IMemberReferenceOperation member => member.Instance,
        IArrayElementReferenceOperation element => element.ArrayReference,
        _ => null,
    };

    /// <summary>沿着成员、元素及作为别名的局部变量，返回持有对象的 Grain 状态值。</summary>
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

    /// <summary>当声明是局部变量的唯一赋值时，返回该局部变量持有的 Grain 状态值。</summary>
    private static IOperation? GetAliasedStateValue(ILocalReferenceOperation reference, DurableTaskSymbols symbols) =>
        reference.SemanticModel is { } semanticModel
        && DurableTaskSymbols.GetOnlyValue(reference.Local, semanticModel) is { } value
        && symbols.IsStateValue(DurableTaskSymbols.Unwrap(value))
            ? DurableTaskSymbols.Unwrap(value)
            : null;
}
