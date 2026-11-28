using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 报告从不运行、运行多次，或从不具备持久化发件箱的代码中投递的持久化任务定义，
/// 以及无人观察其结果的已调度子任务句柄。
/// </summary>
/// <remarks>
/// 返回 <c>DurableTask</c> 的方法创建一个定义；等待、调度或投递该定义时，它才运行。
/// <c>async DurableTask</c> 方法的定义只能使用一次：其状态机运行一次。<c>Post</c> 将一次启动操作暂存到
/// 执行调用代码的 Grain 的持久化发件箱中。在持久化任务体中，<c>ScheduleAsync</c>
/// 启动附属子任务，由当前执行实例拥有。局部变量分析仅报告确定的情况：如果局部变量的
/// 任意引用将其传递给其他代码，或代码再次对其赋值，则视为已观察到该定义。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskDefinitionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        DurableTaskDiagnostics.UnobservedDefinition,
        DurableTaskDiagnostics.ReusedDefinition,
        DurableTaskDiagnostics.PostWithoutOutbox,
        DurableTaskDiagnostics.UnobservedHandle,
        DurableTaskDiagnostics.DetachedLocalTask);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (DurableTaskSymbols.Create(start.Compilation) is not { } symbols)
            {
                return;
            }

            start.RegisterOperationAction(operation => AnalyzeStatement(operation, symbols), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(operation => AnalyzePost(operation, symbols), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzeDetached(operation, symbols), OperationKind.Invocation);
            start.RegisterOperationBlockAction(block => AnalyzeLocals(block, symbols));
        });
    }

    private static void AnalyzeStatement(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var value = ((IExpressionStatementOperation)context.Operation).Operation;
        if (value is ISimpleAssignmentOperation { Target: IDiscardOperation } discard)
        {
            value = discard.Value;
        }

        if (IsDefinition(value, symbols))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DurableTaskDiagnostics.UnobservedDefinition,
                context.Operation.Syntax.GetLocation(),
                GetDefinitionName(value)));
        }
        else if (GetOwnedSchedule(value, context.ContainingSymbol, symbols) is { } schedule)
        {
            ReportUnobservedHandle(context.ReportDiagnostic, schedule);
        }
    }

    /// <summary>报告 Grain 和持久化任务体之外的 <c>Post</c> 调用，此处没有持久化发件箱可暂存它。</summary>
    private static void AnalyzePost(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!DurableTaskSymbols.IsPost(invocation)
            || symbols.IsGrain(context.ContainingSymbol.ContainingType)
            || symbols.IsInDurableBody(invocation, context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DurableTaskDiagnostics.PostWithoutOutbox,
            invocation.Syntax.GetLocation(),
            DurableTaskSymbols.GetExtensionReceiver(invocation) is { } receiver ? GetDefinitionName(receiver) : invocation.TargetMethod.Name));
    }

    /// <summary>
    /// 报告对某个定义使用 <c>Detached</c>，但该定义作为启动它的执行实例的一部分运行：例如 <c>DurableTask</c>
    /// 工厂（如 <c>DurableTask.Run</c>）或组合器，以及 <c>async DurableTask</c> 方法。其他定义（例如
    /// 持久化 Grain 调用）作为根任务启动。
    /// </summary>
    private static void AnalyzeDetached(OperationAnalysisContext context, DurableTaskSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Detached"
            || !DurableTaskSymbols.IsDurableTasksType(invocation.TargetMethod.ContainingType)
            || DurableTaskSymbols.GetExtensionReceiver(invocation) is not { } receiver
            || DurableTaskSymbols.GetDefinition(receiver, "Detached", out _) is not IInvocationOperation definition
            || !RunsInStartingExecution(definition.TargetMethod, symbols))
        {
            return;
        }

        var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            ? access.Name.GetLocation()
            : invocation.Syntax.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(DurableTaskDiagnostics.DetachedLocalTask, location, GetDefinitionName(definition)));
    }

    private static bool RunsInStartingExecution(IMethodSymbol method, DurableTaskSymbols symbols) =>
        method.IsAsync
        || SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.DurableTask)
        || (DurableTaskSymbols.IsDurableTasksType(method.ContainingType) && method.ContainingType.Name is "DurableTaskExtensions" or "DurableSignal");

    /// <summary>返回某个值是否创建定义或配置定义。</summary>
    private static bool IsDefinition(IOperation value, DurableTaskSymbols symbols) =>
        DurableTaskSymbols.Unwrap(value) is IInvocationOperation invocation && symbols.ReturnsDefinition(invocation.TargetMethod);

    /// <summary>穿过任意配置调用，返回定义任务的方法名称。</summary>
    private static string GetDefinitionName(IOperation value) =>
        DurableTaskSymbols.GetDefinition(value, "WithId", out _) switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.Name,
            var definition => definition.Syntax.ToString(),
        };

    /// <summary>
    /// 返回某个值启动由持久化任务体拥有的子任务时所等待的 <c>ScheduleAsync</c> 调用，即
    /// 在持久化任务体中调度未分离的定义。
    /// </summary>
    private static IInvocationOperation? GetOwnedSchedule(IOperation value, ISymbol containingSymbol, DurableTaskSymbols symbols)
    {
        if (DurableTaskSymbols.Unwrap(value) is not IAwaitOperation await
            || DurableTaskSymbols.Unwrap(await.Operation) is not IInvocationOperation { TargetMethod.Name: "ScheduleAsync" } schedule
            || !DurableTaskSymbols.IsDurableTasksType(schedule.TargetMethod.ContainingType)
            || !(DurableTaskSymbols.IsDurableTasksType(await.Type) && await.Type!.Name == "ScheduledTask")
            || DurableTaskSymbols.GetExtensionReceiver(schedule) is not { } receiver)
        {
            return null;
        }

        DurableTaskSymbols.GetDefinition(receiver, "Detached", out var detached);
        return detached || !symbols.IsInDurableBody(await, containingSymbol) ? null : schedule;
    }

    private static void ReportUnobservedHandle(Action<Diagnostic> report, IInvocationOperation schedule) =>
        report(Diagnostic.Create(
            DurableTaskDiagnostics.UnobservedHandle,
            schedule.Syntax.GetLocation(),
            GetDefinitionName(DurableTaskSymbols.GetExtensionReceiver(schedule)!)));

    /// <summary>
    /// 报告持有从未运行的定义，或持有无人观察的附接子任务句柄的局部变量，并
    /// 报告持有单次使用定义的局部变量的第二次运行：第二次引用该变量，或在并不创建定义的循环中
    /// 引用该变量。
    /// </summary>
    private static void AnalyzeLocals(OperationBlockAnalysisContext context, DurableTaskSymbols symbols)
    {
        var definitions = new Dictionary<ILocalSymbol, IVariableDeclaratorOperation>(SymbolEqualityComparer.Default);
        var singleUse = new Dictionary<ILocalSymbol, SyntaxNode>(SymbolEqualityComparer.Default);
        var handles = new Dictionary<ILocalSymbol, IInvocationOperation>(SymbolEqualityComparer.Default);
        var aliases = new Dictionary<IOperation, ILocalSymbol>();
        var reassigned = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var references = new Dictionary<ILocalSymbol, List<ILocalReferenceOperation>>(SymbolEqualityComparer.Default);
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.Descendants())
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation { Initializer.Value: { } initializer } declarator:
                        if (DurableTaskSymbols.GetDefinition(initializer, "WithId", out _) is ILocalReferenceOperation source
                            && symbols.IsDefinitionType(source.Type))
                        {
                            aliases[source] = declarator.Symbol;
                        }
                        else if (IsDefinition(initializer, symbols))
                        {
                            definitions[declarator.Symbol] = declarator;
                        }

                        if (IsSingleUseDefinition(initializer, symbols))
                        {
                            singleUse[declarator.Symbol] = declarator.Syntax;
                        }


                        if (GetOwnedSchedule(initializer, context.OwningSymbol, symbols) is { } schedule)
                        {
                            handles[declarator.Symbol] = schedule;
                        }

                        break;
                    case ISimpleAssignmentOperation { Target: ILocalReferenceOperation target }:
                        reassigned.Add(target.Local);
                        break;
                    case ILocalReferenceOperation reference when !reference.IsDeclaration && !IsAssignmentTarget(reference):
                        if (!references.TryGetValue(reference.Local, out var list))
                        {
                            references[reference.Local] = list = [];
                        }

                        list.Add(reference);
                        break;
                }
            }
        }

        foreach (var pair in definitions)
        {
            if (IsUnobserved(pair.Key, []))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DurableTaskDiagnostics.UnobservedDefinition,
                    pair.Value.Syntax.GetLocation(),
                    GetDefinitionName(pair.Value.Initializer!.Value)));
            }
        }

        foreach (var pair in handles)
        {
            if (!reassigned.Contains(pair.Key) && (!references.TryGetValue(pair.Key, out var uses) || uses.TrueForAll(IsPropertyRead)))
            {
                ReportUnobservedHandle(context.ReportDiagnostic, pair.Value);
            }
        }

        foreach (var pair in singleUse)
        {
            if (reassigned.Contains(pair.Key) || !references.TryGetValue(pair.Key, out var uses))
            {
                continue;
            }

            for (var index = 0; index < uses.Count; index++)
            {
                if (index > 0 || IsInLoopWithout(uses[index].Syntax, pair.Value))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DurableTaskDiagnostics.ReusedDefinition,
                        uses[index].Syntax.GetLocation(),
                        pair.Key.Name));
                }
            }
        }

        // 若局部变量从未被再次赋值，且它的所有引用只用于初始化其他别名，
        // 而这些别名的定义也从未运行，则该局部变量的定义从不运行。
        bool IsUnobserved(ILocalSymbol local, HashSet<ILocalSymbol> visited)
        {
            if (!visited.Add(local) || reassigned.Contains(local))
            {
                return false;
            }

            if (references.TryGetValue(local, out var uses))
            {
                foreach (var use in uses)
                {
                    if (!aliases.TryGetValue(use, out var alias) || !IsUnobserved(alias, visited))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    private static bool IsPropertyRead(ILocalReferenceOperation reference) =>
        reference.Parent is IPropertyReferenceOperation property && property.Instance == reference && !IsAssignmentTarget(property);

    /// <summary>
    /// 返回某个值是否为单次使用的定义：即调用当前编译中返回
    /// <c>DurableTask</c> 的异步方法，可选地通过 <c>WithId</c>、<c>Detached</c> 或 <c>Shielded</c> 进行配置。
    /// </summary>
    private static bool IsSingleUseDefinition(IOperation value, DurableTaskSymbols symbols) =>
        DurableTaskSymbols.GetDefinition(value, "WithId", out _) is IInvocationOperation invocation && symbols.IsDurableBody(invocation.TargetMethod);

    private static bool IsAssignmentTarget(IOperation reference) =>
        reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference;

    private static bool IsInLoopWithout(SyntaxNode use, SyntaxNode declaration)
    {
        for (var current = use.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                    return !current.Span.Contains(declaration.Span);
                case MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax:
                    return false;
            }
        }

        return false;
    }
}
