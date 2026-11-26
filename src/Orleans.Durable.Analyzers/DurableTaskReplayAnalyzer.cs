using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>
/// 长期任务机构报告代码,如果重弹重运行时其效果或值与运行时间不同,则重复重运行
/// 记录任务操作。
/// </summary>
/// <remarks>
/// <para>
/// 持久任务机构是一种同步方法、局部功能或返回的羊羔 <c>DurableTask</c>代表,代表
/// <c>DurableTask.Run</c> 分析仪建立
/// 每个物体的控制流程图, 将其代码分隔为重弹区域, 从此区域中可持久操作
/// 和尾巴,它只在身体完整运行的运行中运行。重放再运行重播放区域
/// 事后行动实施后,有关重播的规则适用于该地。
/// </para>
/// <para>
/// 持久行动是指等待一项持久的任务、一个组合或抽象 API;等待一名助手在
/// 同一汇编,其来源进行耐久作业;或具有 <c>DurableTask.RunInline</c> 效应。
/// 内部程序:身体在不等待的情况下召唤的助手和其他集会的助手,没有受到检查。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableTaskReplayAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        DurableTaskDiagnostics.GrainStateInReplayedCode,
        DurableTaskDiagnostics.NonDeterministicValueInReplayedCode,
        DurableTaskDiagnostics.ExternalAwaitInReplayedCode,
        DurableTaskDiagnostics.StateChangeBeforeExternalAwait,
        DurableTaskDiagnostics.RunInlineAssignsCapturedVariable,
        DurableTaskDiagnostics.ReservedCharacterInName,
        DurableTaskDiagnostics.DuplicateName,
        DurableTaskDiagnostics.SchedulerEscape,
        DurableTaskDiagnostics.NonDurableCombinator,
        DurableTaskDiagnostics.NonDurableDelay,
        DurableTaskDiagnostics.UnfilteredCancellationHandler,
        DurableTaskDiagnostics.AsyncVoidInBody,
        DurableTaskDiagnostics.RepeatedSignalWait,
        DurableTaskDiagnostics.ReplayedLog,
        DurableTaskDiagnostics.PollInBody,
        DurableTaskDiagnostics.ContinuationOffScheduler,
        DurableTaskDiagnostics.OperationInEffect);

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

            var helpers = new SourceInspector();
            start.RegisterOperationBlockAction(block =>
            {
                foreach (var operation in block.OperationBlocks)
                {
                    // 构建控制流程图是分析器的主要成本,所以使用的方法不会持久任务 API
                    // 跳过:它们没有包含任何身体、效果或辅助者, 规则检查。
                    if (operation is IMethodBodyBaseOperation or IBlockOperation { Parent: null or IMethodBodyBaseOperation }
                        && block.OwningSymbol is IMethodSymbol method
                        && (symbols.IsDurableBody(method) || UsesDurableTasks(symbols, operation)))
                    {
                        var graph = block.GetControlFlowGraph(operation);
                        new BodyAnalysis(block, symbols, helpers).AnalyzeGraph(graph, ClassifyMethod(symbols, helpers, method, graph));
                    }
                }
            });
        });
    }

    /// <summary>
    /// 返回运行树使用耐久任务 API:它呼叫或等待持久任务的成员
    /// 命名空间,或包含一个耐久的任务机构。
    /// </summary>
    private static bool UsesDurableTasks(DurableTaskSymbols symbols, IOperation root)
    {
        foreach (var operation in root.DescendantsAndSelf())
        {
            switch (operation)
            {
                case IInvocationOperation invocation when symbols.IsInDurableTasksNamespace(invocation.TargetMethod.ContainingType):
                case IAwaitOperation await when symbols.IsInDurableTasksNamespace(await.Operation.Type):
                case IAnonymousFunctionOperation function when symbols.IsDurableBody(function.Symbol):
                case ILocalFunctionOperation localFunction when symbols.IsDurableBody(localFunction.Symbol):
                    return true;
            }
        }

        return false;
    }

    private enum FunctionKind
    {
        /// <summary>代码不是长久任务机构的一部分</summary>
        None,

        /// <summary>
        /// 由重编规则分析的持久任务机构: <c>async DurableTask</c> 方法、本地函数或羊羔
        /// 或 <c>DurableTask.Run</c> 所接收的代表。
        /// </summary>
        Body,

        /// <summary>
        /// 等待持久任务直接或记录效果的代码,重编规则将分析成一个机构。它可以运行
        /// 或在一项长期任务中,或在一项长期任务之外, <c>WithId</c> 命名根任务。
        /// </summary>
        Helper,

        /// <summary>A <c>DurableTask.RunInline</c> 效果,它运行一次。</summary>
        Effect,

        /// <summary>一只作为创造它的附着体的供述的一部分的羊羔。</summary>
        Inline,
    }

    private static FunctionKind ClassifyMethod(DurableTaskSymbols symbols, SourceInspector helpers, IMethodSymbol method, ControlFlowGraph graph)
    {
        if (symbols.IsDurableBody(method))
        {
            return FunctionKind.Body;
        }

        return PerformsDirectDurableOperations(symbols, graph) ? FunctionKind.Helper : FunctionKind.None;
    }

    /// <summary>
    /// 返回一个图形是等待持久任务还是直接配置,还是记录效果,只有代码
    /// 运行中的任务要持久。
    /// </summary>
    private static bool PerformsDirectDurableOperations(DurableTaskSymbols symbols, ControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
        {
            foreach (var root in RootOperations(block))
            {
                foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(root))
                {
                    switch (operation)
                    {
                        case IAwaitOperation await when IsDefinitionAwait(symbols, await):
                        case IInvocationOperation invocation when symbols.IsRunInline(invocation):
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsDefinitionAwait(DurableTaskSymbols symbols, IAwaitOperation await)
    {
        var type = DurableTaskSymbols.Unwrap(await.Operation).Type;
        return symbols.IsDurableTask(type)
            || (DurableTaskSymbols.IsDurableTasksType(type) && type!.Name == "ConfiguredDurableTask");
    }

    private static IEnumerable<IOperation> RootOperations(BasicBlock block)
    {
        foreach (var operation in block.Operations)
        {
            yield return operation;
        }

        if (block.BranchValue is { } branchValue)
        {
            yield return branchValue;
        }
    }

    /// <summary>检查一个机构等待的帮助者的来源,以寻找从事持久行动的帮助者。</summary>
    private sealed class SourceInspector
    {
        private readonly ConcurrentDictionary<IMethodSymbol, bool> _durableHelpers = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<ISymbol, bool> _replaySafeLoggers = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// 返回本汇编中的方法是否执行持久操作,从来源判断:它调用
        /// <c>WithId</c>, <c>Post</c>, <c>ScheduleAsync</c>,或耐久信号发送,或
        /// <c>DurableTask</c>.
        /// </summary>
        public bool IsDurableHelper(IMethodSymbol method) => _durableHelpers.GetOrAdd(method.OriginalDefinition, static method =>
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                foreach (var invocation in reference.GetSyntax().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    switch (invocation.Expression)
                    {
                        case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "DurableTask" } }:
                            return true;
                        case MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } when IsDurableOperationName(name):
                            return true;
                    }
                }
            }

            return false;
        });

        /// <summary>
        /// 返回字段、财产或当地持有重放安全记录器的地块、财产或地方是否返回:所有任务,包括
        /// 启动器,调用 <c>ReplaySafe</c>。
        /// </summary>
        public bool IsReplaySafeLogger(ISymbol member) => _replaySafeLoggers.GetOrAdd(member, static member =>
        {
            var assignments = 0;
            foreach (var value in AssignedValues(member))
            {
                assignments++;
                if (!CallsReplaySafe(value))
                {
                    return false;
                }
            }

            return assignments > 0;
        });

        private static bool IsDurableOperationName(string name) =>
            name is "WithId" or "Post" or "ScheduleAsync" or "SignalDurableTaskAsync" or "TrySignalDurableTaskAsync";

        private static bool CallsReplaySafe(SyntaxNode value) =>
            value.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(static invocation => invocation.Expression switch
            {
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ReplaySafe" } => true,
                IdentifierNameSyntax { Identifier.ValueText: "ReplaySafe" } => true,
                _ => false,
            });

        private static IEnumerable<SyntaxNode> AssignedValues(ISymbol member)
        {
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                switch (reference.GetSyntax())
                {
                    case VariableDeclaratorSyntax { Initializer.Value: { } initializer }:
                        yield return initializer;
                        break;
                    case PropertyDeclarationSyntax { Initializer.Value: { } initializer }:
                        yield return initializer;
                        break;
                    case PropertyDeclarationSyntax { ExpressionBody.Expression: { } body }:
                        yield return body;
                        break;
                }
            }

            if (member.Kind is SymbolKind.Local)
            {
                yield break;
            }

            foreach (var typeReference in member.ContainingType.DeclaringSyntaxReferences)
            {
                foreach (var assignment in typeReference.GetSyntax().DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    var target = assignment.Left switch
                    {
                        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: var name } => name.Identifier.ValueText,
                        _ => null,
                    };
                    if (target == member.Name)
                    {
                        yield return assignment.Right;
                    }
                }
            }
        }
    }

    /// <summary>在一个行动区分析持久任务机构。</summary>
    private sealed class BodyAnalysis
    {
        private readonly OperationBlockAnalysisContext context;
        private readonly DurableTaskSymbols symbols;
        private readonly SourceInspector helpers;
        private readonly HashSet<(string Id, Location Location)> _reported = [];

        public BodyAnalysis(OperationBlockAnalysisContext context, DurableTaskSymbols symbols, SourceInspector helpers)
        {
            this.context = context;
            this.symbols = symbols;
            this.helpers = helpers;
        }

        public void AnalyzeGraph(ControlFlowGraph graph, FunctionKind kind)
        {
            switch (kind)
            {
                case FunctionKind.Body or FunctionKind.Helper:
                    new Region(this, graph, kind).Analyze();
                    AnalyzeCancellationHandlers(graph.OriginalOperation);
                    break;
                case FunctionKind.Effect:
                    AnalyzeEffect(graph);
                    break;
            }

            var analyzed = kind is FunctionKind.Body or FunctionKind.Helper;
            foreach (var localFunction in graph.LocalFunctions)
            {
                var localGraph = graph.GetLocalFunctionControlFlowGraph(localFunction, context.CancellationToken);
                if (analyzed && localFunction.IsAsync && localFunction.ReturnsVoid)
                {
                    Report(DurableTaskDiagnostics.AsyncVoidInBody, localFunction.Locations[0], localFunction.Name);
                }

                AnalyzeGraph(localGraph, ClassifyMethod(symbols, helpers, localFunction, localGraph));
            }

            foreach (var function in AnonymousFunctions(graph))
            {
                var functionGraph = graph.GetAnonymousFunctionControlFlowGraph(function, context.CancellationToken);
                var functionKind = ClassifyAnonymousFunction(function, functionGraph, kind);

                // ORLEANS0222 报告使 DurableTask.RunInline 和 DurableTask.Run 所接收的代表无法相互联系。
                if (analyzed && function.Symbol.IsAsync && function.Symbol.ReturnsVoid && !IsDurableTaskDelegate(function))
                {
                    Report(DurableTaskDiagnostics.AsyncVoidInBody, function.Syntax.GetLocation(), "async lambda");
                }

                AnalyzeGraph(functionGraph, functionKind);
            }
        }

        private bool IsDurableTaskDelegate(IFlowAnonymousFunctionOperation function) =>
            DurableTaskSymbols.GetDelegateReceiver(function) is { } invocation && (symbols.IsRunInline(invocation) || symbols.IsRun(invocation));

        private FunctionKind ClassifyAnonymousFunction(IFlowAnonymousFunctionOperation function, ControlFlowGraph graph, FunctionKind enclosing)
        {
            if (DurableTaskSymbols.GetDelegateReceiver(function) is { } invocation)
            {
                if (symbols.IsRunInline(invocation))
                {
                    return FunctionKind.Effect;
                }

                if (symbols.IsRun(invocation))
                {
                    return FunctionKind.Body;
                }
            }

            if (symbols.IsDurableBody(function.Symbol))
            {
                return FunctionKind.Body;
            }

            if (enclosing is FunctionKind.Body or FunctionKind.Helper or FunctionKind.Inline)
            {
                return FunctionKind.Inline;
            }

            return PerformsDirectDurableOperations(symbols, graph) ? FunctionKind.Helper : FunctionKind.None;
        }

        private static IEnumerable<IFlowAnonymousFunctionOperation> AnonymousFunctions(ControlFlowGraph graph)
        {
            foreach (var block in graph.Blocks)
            {
                foreach (var root in RootOperations(block))
                {
                    foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(root))
                    {
                        if (operation is IFlowAnonymousFunctionOperation function)
                        {
                            yield return function;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 报告在 <c>DurableTask.RunInline</c> 效果、对身体变量的指定和对象的变化
        /// 并且得维持者,能持久地运作,能逃避天谴。
        /// </summary>
        private void AnalyzeEffect(ControlFlowGraph graph)
        {
            var effect = graph.OriginalOperation switch
            {
                IAnonymousFunctionOperation anonymous => anonymous.Symbol,
                _ => null,
            };

            foreach (var block in graph.Blocks)
            {
                foreach (var root in RootOperations(block))
                {
                    foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(root))
                    {
                        CheckSchedulerEscape(operation);
                        if (operation is IInvocationOperation invocation && symbols.IsOperationOutsideEffects(invocation))
                        {
                            Report(DurableTaskDiagnostics.OperationInEffect, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name);
                        }

                        var target = operation switch
                        {
                            ISimpleAssignmentOperation assignment => assignment.Target,
                            ICompoundAssignmentOperation assignment => assignment.Target,
                            IIncrementOrDecrementOperation increment => increment.Target,
                            ICoalesceAssignmentOperation assignment => assignment.Target,

                            // 地点的改变,例如列入清单的项目也是一种产出。
                            IInvocationOperation { Instance: { } instance } changed when DurableTaskSymbols.IsCollectionMutation(changed.TargetMethod) => instance,
                            _ => null,
                        };
                        ISymbol? variable = (target is null ? null : RootOf(target)) switch
                        {
                            ILocalReferenceOperation local => local.Local,
                            IParameterReferenceOperation parameter => parameter.Parameter,
                            _ => null,
                        };
                        if (variable is not null
                            && effect is not null
                            && !SymbolEqualityComparer.Default.Equals(variable.ContainingSymbol, effect)
                            && !IsDeclaredIn(variable, effect))
                        {
                            Report(DurableTaskDiagnostics.RunInlineAssignsCapturedVariable, operation.Syntax.GetLocation(), variable.Name);
                        }
                    }
                }
            }

            // 指定或更改通过成员访问权限(如 order.Lines)达到的变量。
            static IOperation RootOf(IOperation target)
            {
                var current = DurableTaskSymbols.Unwrap(target);
                while (current is IMemberReferenceOperation { Instance: { } instance } && instance is not IInstanceReferenceOperation)
                {
                    current = DurableTaskSymbols.Unwrap(instance);
                }

                return current;
            }
        }

        private static bool IsDeclaredIn(ISymbol variable, IMethodSymbol function)
        {
            for (var container = variable.ContainingSymbol; container is not null; container = container.ContainingSymbol)
            {
                if (SymbolEqualityComparer.Default.Equals(container, function))
                {
                    return true;
                }

                if (container is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 处理报告者在不过滤的情况下,围绕持久操作而取消
        /// <c>IsTaskCancellationRequested</c> ,不重新投放。
        /// </summary>
        private void AnalyzeCancellationHandlers(IOperation body)
        {
            foreach (var @try in OperationsOutsideNestedFunctions(body).OfType<ITryOperation>())
            {
                if (!OperationsOutsideNestedFunctions(@try.Body).Any(IsDurableOperation))
                {
                    continue;
                }

                foreach (var handler in @try.Catches)
                {
                    if (CatchesCancellation(handler.ExceptionType)
                        && !(handler.Filter is { } filter && (ReferencesIsTaskCancellationRequested(filter) || ExcludesCancellation(filter)))
                        && !OperationsOutsideNestedFunctions(handler.Handler).OfType<IThrowOperation>().Any()
                        && handler.Syntax is CatchClauseSyntax clause)
                    {
                        Report(DurableTaskDiagnostics.UnfilteredCancellationHandler, clause.CatchKeyword.GetLocation());
                    }
                }
            }
        }

        /// <summary>
        /// 返回过滤器是否排除了每个 <c>OperationCanceledException</c>,例如
        /// <c>e is not OperationCanceledException</c> 或 <c>!(e is OperationCanceledException)</c>,单独或作为
        /// <c>&amp;&amp;</c>,因此处理者从不运行取消。
        /// </summary>
        private bool ExcludesCancellation(IOperation filter)
        {
            switch (DurableTaskSymbols.Unwrap(filter))
            {
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and:
                    return ExcludesCancellation(and.LeftOperand) || ExcludesCancellation(and.RightOperand);
                case IIsPatternOperation { Pattern: INegatedPatternOperation { Pattern: var excluded } }:
                    return MatchesEveryCancellation(excluded);
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: var operand }:
                    return DurableTaskSymbols.Unwrap(operand) switch
                    {
                        IIsTypeOperation isType => CoversCancellation(isType.TypeOperand),
                        IIsPatternOperation isPattern => MatchesEveryCancellation(isPattern.Pattern),
                        _ => false,
                    };
                default:
                    return false;
            }

            bool MatchesEveryCancellation(IPatternOperation pattern) => pattern switch
            {
                ITypePatternOperation type => CoversCancellation(type.MatchedType),
                IDeclarationPatternOperation { MatchedType: var type } => CoversCancellation(type),
                _ => false,
            };
        }

        /// <summary>返回返回类型是否 <c>OperationCanceledException</c> 或其基数类型之一。</summary>
        private bool CoversCancellation(ITypeSymbol? type)
        {
            for (var current = symbols.OperationCanceledException; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, type))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CatchesCancellation(ITypeSymbol? type)
        {
            if (type is null || symbols.OperationCanceledException is not { } cancellation)
            {
                return true;
            }

            for (var current = cancellation; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, type))
                {
                    return true;
                }
            }

            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, cancellation))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ReferencesIsTaskCancellationRequested(IOperation filter) =>
            DurableTaskSymbols.DescendantsAndSelf(filter).Any(operation =>
                operation is IPropertyReferenceOperation { Property: var property }
                && SymbolEqualityComparer.Default.Equals(property, symbols.IsTaskCancellationRequested));

        private static IEnumerable<IOperation> OperationsOutsideNestedFunctions(IOperation root)
        {
            var stack = new Stack<IOperation>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var operation = stack.Pop();
                yield return operation;
                foreach (var child in operation.ChildOperations)
                {
                    if (child is not (IAnonymousFunctionOperation or ILocalFunctionOperation))
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        /// <summary>返回在控制流程图之外的一个操作是否为持久操作。</summary>
        private bool IsDurableOperation(IOperation operation) => operation switch
        {
            IAwaitOperation await => IsDurableAwait(await),
            IInvocationOperation invocation => symbols.IsRunInline(invocation) || DurableTaskSymbols.IsPost(invocation),
            _ => false,
        };

        /// <summary>返回,无论是等待者直接或通过协助人员观察持久行动,还是通过这一汇编。</summary>
        private bool IsDurableAwait(IAwaitOperation await)
        {
            if (symbols.IsDurableAwait(await))
            {
                return true;
            }

            var awaited = DurableTaskSymbols.Unwrap(await.Operation);
            if (awaited is IInvocationOperation invocation)
            {
                var method = invocation.TargetMethod;
                if (HasDurableTypeArgument(method.ContainingType)
                    || (invocation.Instance is { } instance && HasDurableTypeArgument(DurableTaskSymbols.Unwrap(instance).Type)))
                {
                    // 综合列举持久任务,例如 DurableTask.WhenEach,包括处理。
                    return true;
                }

                return !method.DeclaringSyntaxReferences.IsEmpty && helpers.IsDurableHelper(method);
            }

            return false;
        }

        private static bool HasDurableTypeArgument(ITypeSymbol? type) =>
            type is INamedTypeSymbol { TypeArguments: var typeArguments } && typeArguments.Any(DurableTaskSymbols.IsDurableTasksType);

        /// <summary>返回等待是否在任务记录操作之外等待工作。</summary>
        private bool IsExternalAwait(IAwaitOperation await)
        {
            if (IsDurableAwait(await))
            {
                return false;
            }

            var awaited = DurableTaskSymbols.Unwrap(await.Operation);
            return awaited switch
            {
                IInvocationOperation { TargetMethod: { Name: "Yield", ContainingType: var type } } when IsTask(type) => false,
                IPropertyReferenceOperation { Property.Name: "CompletedTask" } => false,

                // ORLEANS0225 报告民意调查。
                IInvocationOperation invocation when DurableTaskSymbols.IsPoll(invocation) => false,
                _ => true,
            };
        }

        private bool IsTask(ITypeSymbol? type) => SymbolEqualityComparer.Default.Equals(type, symbols.Task);

        private void CheckSchedulerEscape(IOperation operation)
        {
            if (IsSchedulerEscape(operation))
            {
                var description = operation switch
                {
                    IInvocationOperation invocation => $"{invocation.TargetMethod.ContainingType.Name}.{invocation.TargetMethod.Name}",
                    _ => $"new {operation.Type!.Name}",
                };
                Report(DurableTaskDiagnostics.SchedulerEscape, operation.Syntax.GetLocation(), description);
            }
        }

        /// <summary>返回操作是否从身体的调度器开始工作,例如线索或线状集合工作。</summary>
        private bool IsSchedulerEscape(IOperation operation)
        {
            switch (operation)
            {
                case IInvocationOperation invocation:
                    var method = invocation.TargetMethod;
                    var type = method.ContainingType;
                    return (IsTask(type) && method.Name == "Run")
                        || (SymbolEqualityComparer.Default.Equals(type, symbols.TaskFactory) && method.Name == "StartNew")
                        || SymbolEqualityComparer.Default.Equals(type, symbols.Parallel)
                        || (SymbolEqualityComparer.Default.Equals(type, symbols.ThreadPool) && method.Name.Contains("QueueUserWorkItem"));
                case IObjectCreationOperation creation:
                    return SymbolEqualityComparer.Default.Equals(creation.Type, symbols.Thread)
                        || DurableTaskSymbols.IsOneOf(creation.Type, symbols.TimerTypes);
                default:
                    return false;
            }
        }

        private void Report(DiagnosticDescriptor descriptor, Location location, params object?[] arguments)
        {
            if (_reported.Add((descriptor.Id, location)))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
            }
        }

        /// <summary>将一个物体的控制流程图除以其重播区域及其尾部,并适用规则。</summary>
        private sealed class Region
        {
            private readonly BodyAnalysis _analysis;
            private readonly ControlFlowGraph _graph;
            private readonly FunctionKind _kind;
            private readonly List<BasicBlock>[] _successors;
            private readonly bool[] _reachesDurable;
            private readonly bool[] _reachesExternal;
            private readonly List<(string Name, int Block, int Index, Location Location)> _names = [];

            public Region(BodyAnalysis analysis, ControlFlowGraph graph, FunctionKind kind)
            {
                _analysis = analysis;
                _graph = graph;
                _kind = kind;
                _successors = ComputeSuccessors(graph);
                _reachesDurable = ComputeReach(ContainsDurableOperation);
                _reachesExternal = ComputeReach(ContainsExternalAwait);
            }

            private DurableTaskSymbols Symbols => _analysis.symbols;

            public void Analyze()
            {
                foreach (var block in _graph.Blocks)
                {
                    var roots = RootOperations(block).ToArray();
                    for (var index = 0; index < roots.Length; index++)
                    {
                        var replayed = ContainsDurableOperation(roots[index]) || ReachesAfter(block, roots, index, _reachesDurable, ContainsDurableOperation);
                        var position = (block.Ordinal, index);
                        AnalyzeOperation(roots[index], replayed, position, block, roots);
                    }
                }

                ReportDuplicateNames();
            }

            private void AnalyzeOperation(IOperation root, bool replayed, (int Block, int Index) position, BasicBlock block, IOperation[] roots)
            {
                foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(root))
                {
                    AnalyzeSingle(operation, replayed, position, block, roots);
                    if (operation is IFlowAnonymousFunctionOperation function
                        && _analysis.ClassifyAnonymousFunction(function, _graph.GetAnonymousFunctionControlFlowGraph(function, _analysis.context.CancellationToken), FunctionKind.Body) == FunctionKind.Inline)
                    {
                        AnalyzeInlineFunction(_graph.GetAnonymousFunctionControlFlowGraph(function, _analysis.context.CancellationToken), replayed, position, block, roots);
                    }
                }
            }

            private void AnalyzeInlineFunction(ControlFlowGraph graph, bool replayed, (int Block, int Index) position, BasicBlock block, IOperation[] roots)
            {
                foreach (var inner in graph.Blocks)
                {
                    foreach (var root in RootOperations(inner))
                    {
                        foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(root))
                        {
                            AnalyzeSingle(operation, replayed, position, block, roots);
                            if (operation is IFlowAnonymousFunctionOperation function
                                && _analysis.ClassifyAnonymousFunction(function, graph.GetAnonymousFunctionControlFlowGraph(function, _analysis.context.CancellationToken), FunctionKind.Inline) == FunctionKind.Inline)
                            {
                                AnalyzeInlineFunction(graph.GetAnonymousFunctionControlFlowGraph(function, _analysis.context.CancellationToken), replayed, position, block, roots);
                            }
                        }
                    }
                }
            }

            private void AnalyzeSingle(IOperation operation, bool replayed, (int Block, int Index) position, BasicBlock block, IOperation[] roots)
            {
                _analysis.CheckSchedulerEscape(operation);
                switch (operation)
                {
                    case IFieldReferenceOperation or IPropertyReferenceOperation when replayed && Symbols.IsGrainState(operation.Type):
                        _analysis.Report(DurableTaskDiagnostics.GrainStateInReplayedCode, operation.Syntax.GetLocation(), MemberName(operation));
                        break;
                    case IPropertyReferenceOperation property when replayed && DurableTaskSymbols.IsNonDeterministicMember(property.Property) && !IsLogArgument(operation):
                        _analysis.Report(DurableTaskDiagnostics.NonDeterministicValueInReplayedCode, operation.Syntax.GetLocation(), $"{property.Property.ContainingType.Name}.{property.Property.Name}");
                        break;
                    case IObjectCreationOperation { Constructor: { } constructor } when replayed && DurableTaskSymbols.IsNonDeterministicMember(constructor) && !IsLogArgument(operation):
                        _analysis.Report(DurableTaskDiagnostics.NonDeterministicValueInReplayedCode, operation.Syntax.GetLocation(), $"new {constructor.ContainingType.Name}");
                        break;
                    case IAwaitOperation await when replayed && _analysis.IsExternalAwait(await) && !_analysis.IsSchedulerEscape(DurableTaskSymbols.Unwrap(await.Operation)):
                        if (DurableTaskSymbols.Unwrap(await.Operation) is IInvocationOperation { TargetMethod: { Name: "Delay", ContainingType: var delayType } } && _analysis.IsTask(delayType))
                        {
                            _analysis.Report(DurableTaskDiagnostics.NonDurableDelay, await.Syntax.GetLocation(), "Task.Delay");
                        }
                        else
                        {
                            _analysis.Report(DurableTaskDiagnostics.ExternalAwaitInReplayedCode, await.Syntax.GetLocation(), Describe(await.Operation));
                        }

                        break;
                    case IInvocationOperation invocation:
                        AnalyzeInvocation(invocation, replayed, position);
                        break;
                    case ISimpleAssignmentOperation or ICompoundAssignmentOperation or ICoalesceAssignmentOperation or IIncrementOrDecrementOperation when !replayed && IsStateChange(operation, out var changed) && ReachesExternal(position, block, roots):
                        _analysis.Report(DurableTaskDiagnostics.StateChangeBeforeExternalAwait, operation.Syntax.GetLocation(), changed);
                        break;
                }
            }

            private void AnalyzeInvocation(IInvocationOperation invocation, bool replayed, (int Block, int Index) position)
            {
                var method = invocation.TargetMethod;
                if (replayed && DurableTaskSymbols.IsNonDeterministicMember(method) && !IsLogArgument(invocation))
                {
                    _analysis.Report(DurableTaskDiagnostics.NonDeterministicValueInReplayedCode, invocation.Syntax.GetLocation(), $"{method.ContainingType.Name}.{method.Name}");
                }

                if (replayed && method.Name == "Sleep" && SymbolEqualityComparer.Default.Equals(method.ContainingType, Symbols.Thread))
                {
                    _analysis.Report(DurableTaskDiagnostics.NonDurableDelay, invocation.Syntax.GetLocation(), "Thread.Sleep");
                }

                if (replayed && Symbols.IsLog(invocation, out var logger) && !IsReplaySafeLogger(logger))
                {
                    _analysis.Report(DurableTaskDiagnostics.ReplayedLog, invocation.Syntax.GetLocation());
                }

                if (Symbols.IsGrainState(invocation.Instance?.Type) && IsMutating(method) && !replayed && ReachesExternal(position, null, null))
                {
                    _analysis.Report(DurableTaskDiagnostics.StateChangeBeforeExternalAwait, invocation.Syntax.GetLocation(), method.Name);
                }

                if (_analysis.IsTask(method.ContainingType) && method.Name is "WhenAll" or "WhenAny" or "WaitAll" or "WaitAny" && ArgumentsPerformDurableWork(invocation))
                {
                    _analysis.Report(DurableTaskDiagnostics.NonDurableCombinator, invocation.Syntax.GetLocation(), $"Task.{method.Name}");
                }

                if (Symbols.IsSignalWait(invocation) && IsInLoop(invocation.Syntax) && DurableTaskSymbols.GetExtensionReceiver(invocation) is { } signal && !IsDerivedSignal(signal))
                {
                    _analysis.Report(DurableTaskDiagnostics.RepeatedSignalWait, invocation.Syntax.GetLocation(), signal.Syntax.ToString());
                }

                // 投票和无背景续延是外部长期任务机构的有效做法,例如客户等,因此仅
                // 机构报告它们。
                if (_kind == FunctionKind.Body && DurableTaskSymbols.IsPoll(invocation))
                {
                    _analysis.Report(DurableTaskDiagnostics.PollInBody, invocation.Syntax.GetLocation(), method.Name);
                }

                if (_kind == FunctionKind.Body && DurableTaskSymbols.ContinuesOffContext(invocation, out var argument))
                {
                    _analysis.Report(DurableTaskDiagnostics.ContinuationOffScheduler, invocation.Syntax.GetLocation(), argument);
                }

                // WithId 名称仅在一个持久任务机构内识别儿童;在其他地方,如客户中,它只指儿童的身份。
                // root 任务。 DurableTask.RunInline 名称只识别耐久任务的效果,所以可以指定一个助手
                // 记录一在身体中运行。
                if ((Symbols.IsRunInline(invocation) || (_kind == FunctionKind.Body && DurableTaskSymbols.IsWithId(invocation)))
                    && GetName(invocation) is { } name)
                {
                    if (name.Value.IndexOf('$') >= 0)
                    {
                        _analysis.Report(DurableTaskDiagnostics.ReservedCharacterInName, name.Location, name.Value);
                    }
                    else
                    {
                        _names.Add((name.Value, position.Block, position.Index, name.Location));
                    }
                }
            }

            private bool IsReplaySafeLogger(IOperation? logger)
            {
                switch (logger is null ? null : DurableTaskSymbols.Unwrap(logger))
                {
                    case IInvocationOperation { TargetMethod.Name: "ReplaySafe" }:
                        return true;
                    case IFieldReferenceOperation field:
                        return _analysis.helpers.IsReplaySafeLogger(field.Field);
                    case IPropertyReferenceOperation property:
                        return _analysis.helpers.IsReplaySafeLogger(property.Property);
                    case ILocalReferenceOperation local:
                        return _analysis.helpers.IsReplaySafeLogger(local.Local);
                    default:
                        return false;
                }
            }

            /// <summary>
            /// 返回 <c>WithId</c> 或 <c>DurableTask.RunInline</c> 的传教通行证或无效的常数名称。
            /// 未命名 <c>DurableTask.RunInline</c> 使用执行的下一个生成标识符,且没有名称 。
            /// </summary>
            private static (string Value, Location Location)? GetName(IInvocationOperation invocation)
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter is { Type.SpecialType: SpecialType.System_String } && argument.Value.ConstantValue is { HasValue: true, Value: string value })
                    {
                        return (value, argument.Value.Syntax.GetLocation());
                    }
                }

                return null;
            }

            private void ReportDuplicateNames()
            {
                foreach (var group in _names.GroupBy(static name => name.Name))
                {
                    var sites = group.OrderBy(static site => site.Location.SourceSpan.Start).ToArray();
                    for (var later = 1; later < sites.Length; later++)
                    {
                        for (var earlier = 0; earlier < later; earlier++)
                        {
                            if (sites[earlier].Location != sites[later].Location && CanRunInOneRun(sites[earlier], sites[later]))
                            {
                                _analysis.Report(DurableTaskDiagnostics.DuplicateName, sites[later].Location, sites[later].Name);
                                break;
                            }
                        }
                    }
                }
            }

            private bool CanRunInOneRun(
                (string Name, int Block, int Index, Location Location) first,
                (string Name, int Block, int Index, Location Location) second)
            {
                if (first.Block == second.Block)
                {
                    return true;
                }

                return Reaches(first.Block, second.Block) || Reaches(second.Block, first.Block);
            }

            private bool Reaches(int from, int to)
            {
                var visited = new bool[_graph.Blocks.Length];
                var pending = new Stack<int>();
                pending.Push(from);
                while (pending.Count > 0)
                {
                    foreach (var successor in _successors[pending.Pop()])
                    {
                        if (successor.Ordinal == to)
                        {
                            return true;
                        }

                        if (!visited[successor.Ordinal])
                        {
                            visited[successor.Ordinal] = true;
                            pending.Push(successor.Ordinal);
                        }
                    }
                }

                return false;
            }

            private bool ArgumentsPerformDurableWork(IInvocationOperation invocation)
            {
                foreach (var argument in invocation.Arguments)
                {
                    foreach (var operation in DurableTaskSymbols.DescendantsAndSelf(argument.Value))
                    {
                        switch (operation)
                        {
                            case IFlowAnonymousFunctionOperation function:
                                var graph = _graph.GetAnonymousFunctionControlFlowGraph(function, _analysis.context.CancellationToken);
                                if (graph.Blocks.SelectMany(RootOperations).SelectMany(DurableTaskSymbols.DescendantsAndSelf).Any(ContainsDurableOperationShallow))
                                {
                                    return true;
                                }

                                break;
                            case IInvocationOperation call when !call.TargetMethod.DeclaringSyntaxReferences.IsEmpty && _analysis.helpers.IsDurableHelper(call.TargetMethod):
                                return true;
                        }
                    }
                }

                return false;
            }

            private bool IsLogArgument(IOperation operation)
            {
                for (var current = operation.Parent; current is not null; current = current.Parent)
                {
                    if (current is IInvocationOperation invocation && Symbols.IsLog(invocation, out _))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IsStateChange(IOperation assignment, out string changed)
            {
                var target = assignment switch
                {
                    ISimpleAssignmentOperation simple => simple.Target,
                    ICompoundAssignmentOperation compound => compound.Target,
                    ICoalesceAssignmentOperation coalesce => coalesce.Target,
                    IIncrementOrDecrementOperation increment => increment.Target,
                    _ => null,
                };
                if (target is IPropertyReferenceOperation { Instance: { } instance } property && Symbols.IsGrainState(instance.Type))
                {
                    changed = MemberName(instance);
                    return true;
                }

                changed = string.Empty;
                return false;
            }

            // Grain 通过其收藏的状态变化 API,并通过国家具体对日志的作业完成
            // 源,并持续状态。
            private static bool IsMutating(IMethodSymbol method) =>
                DurableTaskSymbols.IsCollectionMutation(method)
                || method.Name is "Append" or "TrimBefore" or "TrySetResult" or "TrySetException" or "TrySetCanceled"
                    or "WriteStateAsync" or "ClearStateAsync";

            private static string MemberName(IOperation operation) => operation switch
            {
                IFieldReferenceOperation field => field.Field.Name,
                IPropertyReferenceOperation property => property.Property.Name,
                _ => operation.Syntax.ToString(),
            };

            private static string Describe(IOperation awaited)
            {
                var text = DurableTaskSymbols.Unwrap(awaited).Syntax.ToString();
                return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
            }

            private static bool IsInLoop(SyntaxNode node)
            {
                for (var current = node.Parent; current is not null; current = current.Parent)
                {
                    switch (current)
                    {
                        case ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                            return true;
                        case MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax:
                            return false;
                    }
                }

                return false;
            }

            private static bool IsDerivedSignal(IOperation signal) =>
                DurableTaskSymbols.Unwrap(signal) is IInvocationOperation { TargetMethod.Name: "For" };

            private bool ContainsDurableOperation(IOperation root) => DurableTaskSymbols.DescendantsAndSelf(root).Any(ContainsDurableOperationShallow);

            private bool ContainsDurableOperationShallow(IOperation operation) => operation switch
            {
                IAwaitOperation await => _analysis.IsDurableAwait(await),
                IInvocationOperation invocation => Symbols.IsRunInline(invocation) || DurableTaskSymbols.IsPost(invocation),
                _ => false,
            };

            private bool ContainsExternalAwait(IOperation root) =>
                DurableTaskSymbols.DescendantsAndSelf(root).Any(operation => operation is IAwaitOperation await && _analysis.IsExternalAwait(await));

            private bool ReachesAfter(BasicBlock block, IOperation[] roots, int index, bool[] reach, Func<IOperation, bool> contains)
            {
                for (var later = index + 1; later < roots.Length; later++)
                {
                    if (contains(roots[later]))
                    {
                        return true;
                    }
                }

                return _successors[block.Ordinal].Any(successor => reach[successor.Ordinal]);
            }

            private bool ReachesExternal((int Block, int Index) position, BasicBlock? block, IOperation[]? roots)
            {
                block ??= _graph.Blocks[position.Block];
                roots ??= RootOperations(block).ToArray();
                return ReachesAfter(block, roots, position.Index, _reachesExternal, ContainsExternalAwait);
            }

            private bool[] ComputeReach(Func<IOperation, bool> contains)
            {
                var blocks = _graph.Blocks;
                var reach = new bool[blocks.Length];
                foreach (var block in blocks)
                {
                    reach[block.Ordinal] = RootOperations(block).Any(contains);
                }

                for (var changed = true; changed;)
                {
                    changed = false;
                    for (var ordinal = blocks.Length - 1; ordinal >= 0; ordinal--)
                    {
                        if (!reach[ordinal] && _successors[ordinal].Any(successor => reach[successor.Ordinal]))
                        {
                            reach[ordinal] = true;
                            changed = true;
                        }
                    }
                }

                return reach;
            }

            /// <summary>
            /// 计算每个区块的继承人:其分支目的地、其分支经营的最后区域、
            /// 和最终目的地的
            /// </summary>
            private static List<BasicBlock>[] ComputeSuccessors(ControlFlowGraph graph)
            {
                var blocks = graph.Blocks;
                var successors = new List<BasicBlock>[blocks.Length];
                var finallyContinuations = new Dictionary<ControlFlowRegion, List<BasicBlock>>();
                foreach (var block in blocks)
                {
                    var list = successors[block.Ordinal] = [];
                    foreach (var branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
                    {
                        if (branch is null)
                        {
                            continue;
                        }

                        if (branch.Destination is { } destination)
                        {
                            list.Add(destination);
                        }

                        foreach (var region in branch.FinallyRegions)
                        {
                            list.Add(blocks[region.FirstBlockOrdinal]);
                            if (branch.Destination is { } continuation)
                            {
                                if (!finallyContinuations.TryGetValue(region, out var continuations))
                                {
                                    finallyContinuations[region] = continuations = [];
                                }

                                continuations.Add(continuation);
                            }
                        }
                    }

                    for (var region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
                    {
                        if (region.Kind == ControlFlowRegionKind.Try && region.EnclosingRegion is { } tryAndHandlers)
                        {
                            foreach (var handler in tryAndHandlers.NestedRegions)
                            {
                                if (handler != region)
                                {
                                    list.Add(blocks[handler.FirstBlockOrdinal]);
                                }
                            }
                        }
                    }
                }

                foreach (var block in blocks)
                {
                    for (var region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
                    {
                        if (region.Kind == ControlFlowRegionKind.Finally
                            && region.LastBlockOrdinal == block.Ordinal
                            && finallyContinuations.TryGetValue(region, out var continuations))
                        {
                            successors[block.Ordinal].AddRange(continuations);
                        }
                    }
                }

                return successors;
            }
        }
    }
}
