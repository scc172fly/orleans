using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 向代表报告 <c>DurableTask.Run</c> 和 <c>DurableTask.RunInline</c> 接收的,无法运行的电话
/// 意指:一个在工作完成之前即为空无代表的助理代表,以及一个同步步骤,其内线是作为
/// 效果。
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

    /// <summary>返回代表参数通过转换和投影约束的函数或方法。</summary>
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
    /// 返回代表约束的对一个非同步无效函数的描述: Async 羊羔、 a sync 无效方法或 a
    /// 声明初始化为一个且没有代码再次指定的本地。
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
    /// 返回一个步骤的代表是否只做同步工作:它等待什么,不返回任务,不阅读
    /// 步骤收到 <c>CancellationToken</c> 。
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

    /// <summary>返回是否在界定、可选命名或屏蔽而不是分离的阶段等待一个步骤。</summary>
    private static bool IsAwaitedStep(IInvocationOperation run) =>
        DurableTaskSymbols.GetConsumer(run, "Detached", out var detached) is IAwaitOperation && !detached;
}
