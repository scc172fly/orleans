using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 报告 <c>DurableTask.Run</c> 和 <c>DurableTask.RunInline</c> 接收、但无法按调用意图运行的委托：
/// async void 委托（其完成早于实际工作结束），以及同步步骤（它以内联
/// 效果形式运行）。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskDelegateAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        DurableTaskDiagnostics.SynchronousStep,
        DurableTaskDiagnostics.AsyncVoidDelegate);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is { } symbols)
            {
                start.RegisterOperationAction(operation => Analyze(operation, symbols), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var run = symbols.IsRun(invocation);
        if (!run && !symbols.IsRunInline(invocation))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
            {
                continue;
            }

            var target = GetTarget(argument.Value);
            if (invoke.ReturnsVoid && GetAsyncVoidName(target, context.Operation.SemanticModel) is { } name)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableTaskDiagnostics.AsyncVoidDelegate,
                    argument.Value.Syntax.GetLocation(),
                    name,
                    $"DurableTask.{invocation.TargetMethod.Name}"));
            }
            else if (run
                && target is IAnonymousFunctionOperation function
                && IsSynchronous(function)
                && IsAwaitedStep(invocation)
                && symbols.IsInDurableBody(invocation, context.ContainingSymbol))
            {
                var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
                    ? memberAccess.Name.GetLocation()
                    : invocation.Syntax.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(DurableTaskDiagnostics.SynchronousStep, location));
            }
        }
    }

    /// <summary>穿过转换和强制类型转换，返回委托实参绑定的函数或方法。</summary>
    private static IOperation GetTarget(IOperation value)
    {
        while (true)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    value = conversion.Operand;
                    break;
                case IDelegateCreationOperation creation:
                    value = creation.Target;
                    break;
                default:
                    return value;
            }
        }
    }

    /// <summary>
    /// 返回委托绑定的 async void 函数的描述：异步 Lambda 表达式、async void 方法，或
    /// 在声明时以其中之一初始化且从未再次赋值的局部变量。
    /// </summary>
    private static string? GetAsyncVoidName(IOperation target, SemanticModel? semanticModel)
    {
        switch (target)
        {
            case IAnonymousFunctionOperation { Symbol.IsAsync: true }:
                return "async lambda";
            case IMethodReferenceOperation { Method: { IsAsync: true, ReturnsVoid: true } method }:
                return method.Name;
            case ILocalReferenceOperation { Local: var local } when semanticModel is not null
                && DurableTaskSymbols.GetOnlyValue(local, semanticModel) is { } value
                && GetAsyncVoidName(GetTarget(value), semanticModel: null) is not null:
                return local.Name;
            default:
                return null;
        }
    }

    /// <summary>
    /// 返回步骤委托是否仅执行同步工作：不等待任何对象，不返回任务，也不读取
    /// 传给该步骤的 <c>CancellationToken</c>。
    /// </summary>
    private static bool IsSynchronous(IAnonymousFunctionOperation function)
    {
        var symbol = function.Symbol;
        if (!symbol.IsAsync && !symbol.ReturnsVoid && !symbol.ReturnType.GetMembers("GetAwaiter").IsEmpty)
        {
            return false;
        }

        foreach (var operation in function.Body.DescendantsAndSelf())
        {
            switch (operation)
            {
                case IAwaitOperation:
                case IForEachLoopOperation { IsAsynchronous: true }:
                case IUsingOperation { IsAsynchronous: true }:
                case IUsingDeclarationOperation { IsAsynchronous: true }:
                case IParameterReferenceOperation { Parameter.Type.Name: "CancellationToken" } reference
                    when SymbolEqualityComparer.Default.Equals(reference.Parameter.ContainingSymbol, symbol):
                    return false;
            }
        }

        return true;
    }

    /// <summary>返回步骤是否在定义处被等待，可选地命名或屏蔽取消，且未被分离。</summary>
    private static bool IsAwaitedStep(IInvocationOperation run) =>
        DurableTaskSymbols.GetConsumer(run, "Detached", out var detached) is IAwaitOperation && !detached;
}
