using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 长期任务定义,这些定义从不运行、不止一次运行或没有代码发布
/// 耐久的发信箱,以及没有任何观察力的被安排在班儿童的处理器。
/// </summary>
/// <remarks>
/// 返回 <c>DurableTask</c> 的方法创建定义,该定义在等待、计划或张贴时运行。
/// <c>async DurableTask</c> 方法的定义是单用:它曾经运行过国家机器。 <c>Post</c> 级a
/// 开始于运行调用代码的 grain 耐久发件箱中。在一个耐久任务机构, <c>ScheduleAsync</c>
/// 分析当地人只报告明确的案例:
/// 任何参考文件的转接或密码再次指定,按所观察到的编号。
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

    /// <summary>报告a <c>Post</c> 外部 grains 以及没有持久发信箱可提供这种服务的长久任务机构。</summary>
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
    /// 报告 <c>Detached</c> :一个 <c>DurableTask</c>
    /// 工厂,如 <c>DurableTask.Run</c> 或组合器,或 <c>async DurableTask</c> 方法。
    /// 长期和可持久的 grain 调用, 以根任务开头 。
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

    /// <summary>返回一个值是否创建定义或配置。</summary>
    private static bool IsDefinition(IOperation value, DurableTaskSymbols symbols) =>
        DurableTaskSymbols.Unwrap(value) is IInvocationOperation invocation && symbols.ReturnsDefinition(invocation.TargetMethod);

    /// <summary>通过任何配置调用返回定义任务的方法的名称。</summary>
    private static string GetDefinitionName(IOperation value) =>
        DurableTaskSymbols.GetDefinition(value, "WithId", out _) switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.Name,
            var definition => definition.Syntax.ToString(),
        };

    /// <summary>
    /// 返回当一个长期任务机构所拥有孩子开始时, 等待的值的 <c>ScheduleAsync</c> 调用: a
    /// 定义不是分离的, 在一个持久任务机构里安排。
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
    /// 当地人报告,他们持有一个定义,从未经营或支配过任何没有遵守的拥有儿童,
    /// 持有单一使用定义的当地人第二期:第二次引用,或环中提及
    /// 并不会创造定义
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

        // 当本地再也没有被指定且唯一引用初始化时, 本地的定义永远不会运行
        // 其定义也从未起作用的别名。
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
    /// 返回值是否为单一使用定义:在此汇编中号召采用同步方法,返回
    /// <c>DurableTask</c>,可选配置为 <c>WithId</c>、 <c>Detached</c>或 <c>Shielded</c>。
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
