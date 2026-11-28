using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>按元数据名称解析各规则检查的持久化任务、Grain 状态及平台符号。</summary>
/// <remarks>按名称匹配使分析器不依赖抽象层程序集的版本。</remarks>
internal sealed class DurableTaskSymbols
{
    private const string DurableTasksNamespace = "Orleans.Durable.Tasks";

    private readonly ImmutableArray<INamedTypeSymbol> _grainStateTypes;
    private readonly INamedTypeSymbol? _durableValue;
    private readonly INamedTypeSymbol? _persistentState;

    private DurableTaskSymbols(Compilation compilation, INamedTypeSymbol durableTask)
    {
        DurableTask = durableTask;
        DurableSignal = compilation.GetTypeByMetadataName("Orleans.Durable.Tasks.DurableSignal`1");
        DurableVersion = compilation.GetTypeByMetadataName("Orleans.Durable.Tasks.DurableVersion");
        GrainBase = compilation.GetTypeByMetadataName("Orleans.IGrainBase");
        Task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        Thread = compilation.GetTypeByMetadataName("System.Threading.Thread");
        ThreadPool = compilation.GetTypeByMetadataName("System.Threading.ThreadPool");
        Parallel = compilation.GetTypeByMetadataName("System.Threading.Tasks.Parallel");
        TaskFactory = compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskFactory");
        CancellationTokenSource = compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource");
        OperationCanceledException = compilation.GetTypeByMetadataName("System.OperationCanceledException");
        Logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        LoggerExtensions = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerExtensions");
        LoggerMessageAttribute = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerMessageAttribute");
        TimerTypes = Resolve(compilation, "System.Threading.Timer", "System.Timers.Timer", "System.Threading.PeriodicTimer");

        var context = compilation.GetTypeByMetadataName("Orleans.Durable.Tasks.DurableTaskContext");
        IsTaskCancellationRequested = GetProperty(context, "IsTaskCancellationRequested");
        ContextCancellationToken = GetProperty(context, "CancellationToken");

        var stateTypes = Resolve(
            compilation,
            "Orleans.Durable.Journaling.IDurableValue`1",
            "Orleans.Durable.Journaling.IDurableDictionary`2",
            "Orleans.Durable.Journaling.IDurableList`1",
            "Orleans.Durable.Journaling.IDurableQueue`1",
            "Orleans.Durable.Journaling.IDurableSet`1",
            "Orleans.Durable.Journaling.IDurableTaskCompletionSource`1",
            "Orleans.Durable.Journaling.IDurableLog`1",
            "Orleans.Runtime.IPersistentState`1");
        _grainStateTypes = stateTypes;
        _durableValue = compilation.GetTypeByMetadataName("Orleans.Durable.Journaling.IDurableValue`1");
        _persistentState = compilation.GetTypeByMetadataName("Orleans.Runtime.IPersistentState`1");
        NonDeterministicTypes = Resolve(
            compilation,
            "System.DateTime",
            "System.DateTimeOffset",
            "System.Guid",
            "System.Random",
            "System.Security.Cryptography.RandomNumberGenerator",
            "System.Diagnostics.Stopwatch",
            "System.Environment",
            "System.TimeProvider");
    }

    public INamedTypeSymbol DurableTask { get; }

    public INamedTypeSymbol? DurableSignal { get; }

    public INamedTypeSymbol? DurableVersion { get; }

    public INamedTypeSymbol? GrainBase { get; }

    public INamedTypeSymbol? Task { get; }

    public INamedTypeSymbol? Thread { get; }

    public INamedTypeSymbol? ThreadPool { get; }

    public INamedTypeSymbol? Parallel { get; }

    public INamedTypeSymbol? TaskFactory { get; }

    public INamedTypeSymbol? CancellationTokenSource { get; }

    public INamedTypeSymbol? OperationCanceledException { get; }

    public INamedTypeSymbol? Logger { get; }

    public INamedTypeSymbol? LoggerExtensions { get; }

    public INamedTypeSymbol? LoggerMessageAttribute { get; }

    public ImmutableArray<INamedTypeSymbol> TimerTypes { get; }

    /// <summary>获取 <c>DurableTaskContext.IsTaskCancellationRequested</c>，它报告持久化任务取消状态。</summary>
    public IPropertySymbol? IsTaskCancellationRequested { get; }

    /// <summary>
    /// 获取 <c>DurableTaskContext.CancellationToken</c>，任务取消或执行尝试取消都会使该令牌取消。
    /// </summary>
    public IPropertySymbol? ContextCancellationToken { get; }

    public ImmutableArray<INamedTypeSymbol> NonDeterministicTypes { get; }

    /// <summary>返回引用持久化任务抽象层的编译所对应的符号，或 null。</summary>
    public static DurableTaskSymbols? Create(Compilation compilation) =>
        compilation.GetTypeByMetadataName("Orleans.Durable.Tasks.DurableTask") is { } durableTask
            ? new DurableTaskSymbols(compilation, durableTask)
            : null;

    /// <summary>返回某类型是否为 <c>DurableTask</c> 或派生自该类型。</summary>
    public bool IsDurableTask(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, DurableTask))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回某类型是否声明于持久化任务抽象层的命名空间中。</summary>
    public static bool IsDurableTasksType(ITypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() is { } name
        && (name == DurableTasksNamespace || name.StartsWith(DurableTasksNamespace + ".", StringComparison.Ordinal));

    /// <summary>
    /// 返回某类型是否声明于持久化任务抽象层的命名空间或其子命名空间中，比较
    /// 命名空间符号本身，而不格式化其名称。
    /// </summary>
    public bool IsInDurableTasksNamespace(ITypeSymbol? type)
    {
        for (var current = type?.ContainingNamespace; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            if (SymbolEqualityComparer.Default.Equals(current, DurableTask.ContainingNamespace))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回某方法是否返回持久化任务定义或该定义的配置。</summary>
    public bool ReturnsDefinition(IMethodSymbol method) => IsDefinitionType(method.ReturnType);

    /// <summary>返回某类型是否为持久化任务定义或该定义的配置。</summary>
    public bool IsDefinitionType(ITypeSymbol? type) =>
        IsDurableTask(type) || (IsDurableTasksType(type) && type!.Name == "ConfiguredDurableTask");

    /// <summary>返回某类型或包含它的类型是否为 Grain：即是否实现 <c>IGrainBase</c>。</summary>
    public bool IsGrain(INamedTypeSymbol? type)
    {
        if (GrainBase is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var @interface in current.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(@interface, GrainBase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>返回某方法是否为持久化任务体：返回 <c>DurableTask</c> 的异步方法。</summary>
    public bool IsDurableBody(IMethodSymbol method) => method.IsAsync && IsDurableTask(method.ReturnType);

    /// <summary>
    /// 返回某操作是否运行在持久化任务体中：<c>async DurableTask</c> 方法，或由持久化任务工厂
    /// （例如 <c>DurableTask.Run</c>）运行的委托，包括此类任务体包含的代码。
    /// </summary>
    public bool IsInDurableBody(IOperation operation, ISymbol containingSymbol)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation function
                    when IsDurableBody(function.Symbol) || GetDelegateReceiver(function) is { } factory && IsDurableTasksType(factory.TargetMethod.ContainingType):
                    return true;
                case ILocalFunctionOperation local when IsDurableBody(local.Symbol):
                    return true;
            }
        }

        return containingSymbol is IMethodSymbol method && IsDurableBody(method);
    }

    /// <summary>返回将委托作为实参接收的调用，或 null。</summary>
    public static IInvocationOperation? GetDelegateReceiver(IOperation function)
    {
        for (var current = function.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IDelegateCreationOperation:
                case IConversionOperation:
                    continue;
                case IArgumentOperation { Parent: IInvocationOperation invocation }:
                    return invocation;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// 穿过包裹定义的配置调用，返回消费定义的操作，并指示其中是否有
    /// 调用为 <paramref name="configuration"/>。
    /// </summary>
    public static IOperation? GetConsumer(IOperation definition, string configuration, out bool configured)
    {
        configured = false;
        var current = definition;
        while (current.Parent is { } parent)
        {
            if (parent is IConversionOperation)
            {
                current = parent;
                continue;
            }

            var configurationCall = parent switch
            {
                IInvocationOperation invocation when invocation.Instance == current => invocation,
                IArgumentOperation { Parent: IInvocationOperation invocation } argument
                    when invocation.Instance is null && invocation.Arguments[0] == argument => invocation,
                _ => null,
            };
            if (configurationCall is null || !IsConfiguration(configurationCall))
            {
                return parent;
            }

            configured |= configurationCall.TargetMethod.Name == configuration;
            current = configurationCall;
        }

        return null;
    }

    /// <summary>
    /// 穿过包裹定义的配置调用，返回创建定义的操作，并指示其中是否有
    /// 调用为 <paramref name="configuration"/>。
    /// </summary>
    public static IOperation GetDefinition(IOperation value, string configuration, out bool configured)
    {
        configured = false;
        var current = Unwrap(value);
        while (current is IInvocationOperation invocation && IsConfiguration(invocation) && GetExtensionReceiver(invocation) is { } receiver)
        {
            configured |= invocation.TargetMethod.Name == configuration;
            current = Unwrap(receiver);
        }

        return current;
    }

    /// <summary>
    /// 返回包裹定义的配置调用中是否包含 <c>Detached(rootId)</c>；它提供
    /// 任务的根标识，因此启动操作不占用该执行实例生成的标识符。
    /// </summary>
    public static bool SuppliesRootId(IOperation value)
    {
        var current = Unwrap(value);
        while (current is IInvocationOperation invocation && IsConfiguration(invocation) && GetExtensionReceiver(invocation) is { } receiver)
        {
            if (invocation.TargetMethod.Name == "Detached"
                && invocation.TargetMethod.Parameters.Any(static parameter => parameter.Type.SpecialType == SpecialType.System_String))
            {
                return true;
            }

            current = Unwrap(receiver);
        }

        return false;
    }

    /// <summary>
    /// 返回某调用是否创建组合器；等待它的执行实例将使用生成的操作标识将该组合器作为自身的一部分运行：
    /// <c>WhenAll</c>、<c>WhenAny</c>、<c>Race</c>、<c>ScheduleAll</c>、
    /// <c>WhenAllCompleted</c>、定义的 <c>WaitAsync</c> 或 <c>WaitUntilAsync</c>，或信号的 <c>TryWaitAsync</c>。
    /// </summary>
    public bool IsCombinator(IInvocationOperation invocation) =>
        IsDurableTask(invocation.TargetMethod.ReturnType)
        && IsDurableTasksType(invocation.TargetMethod.ContainingType)
        && invocation.TargetMethod.Name is "WhenAll" or "WhenAny" or "Race" or "ScheduleAll" or "WhenAllCompleted" or "WaitAsync"
            or "WaitUntilAsync" or "TryWaitAsync"
        && !(invocation.TargetMethod.Name == "WaitAsync" && IsSignalWait(invocation));

    /// <summary>
    /// 返回某操作是否读取 Grain 状态持有的值：<c>IDurableValue&lt;T&gt;.Value</c>、
    /// <c>IPersistentState&lt;T&gt;.State</c>，或通过索引器访问日志化集合中的元素。
    /// </summary>
    public bool IsStateValue(IOperation operation) =>
        operation is IPropertyReferenceOperation { Instance.Type: { } stateType, Property: var property }
        && (property.IsIndexer
            ? IsGrainState(stateType)
            : (property.Name == "Value" && Implements(stateType, _durableValue))
                || (property.Name == "State" && Implements(stateType, _persistentState)));

    /// <summary>
    /// 返回某方法是否就地修改其接收集合，如 <c>List&lt;T&gt;.Add</c> 所做的那样，
    /// 而不是像不可变集合那样返回修改后的副本。
    /// </summary>
    public static bool IsCollectionMutation(IMethodSymbol method) =>
        !method.IsStatic
        && method.Name is "Add" or "AddRange" or "Remove" or "RemoveAt" or "RemoveAll" or "RemoveRange" or "RemoveWhere" or "Clear"
            or "Insert" or "InsertRange" or "Enqueue" or "Dequeue" or "TryDequeue" or "Push" or "Pop" or "TryPop" or "TryAdd"
            or "TryRemove" or "UnionWith" or "IntersectWith" or "ExceptWith" or "SymmetricExceptWith" or "Sort" or "Reverse"
        && !SymbolEqualityComparer.Default.Equals(method.ReturnType.OriginalDefinition, method.ContainingType.OriginalDefinition)
        && method.ContainingNamespace?.ToDisplayString() != "System.Collections.Immutable";

    /// <summary>返回某类型是否为日志化或持久化 Grain 状态类型，或者实现了这些类型之一。</summary>
    public bool IsGrainState(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || _grainStateTypes.IsEmpty)
        {
            return false;
        }

        if (IsGrainStateDefinition(named))
        {
            return true;
        }

        foreach (var @interface in named.AllInterfaces)
        {
            if (IsGrainStateDefinition(@interface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 返回某个 await 是否观察正在运行任务的持久化操作：被等待的值或其 awaiter 为
    /// 持久化任务类型，或由抽象层 API 产生。
    /// </summary>
    public bool IsDurableAwait(IAwaitOperation await)
    {
        var awaited = Unwrap(await.Operation);
        if (IsDurableTasksType(awaited.Type))
        {
            return true;
        }

        return awaited is IInvocationOperation invocation && IsDurableTasksType(invocation.TargetMethod.ContainingType) && !IsPoll(invocation);
    }

    /// <summary>返回某调用是否记录效果：具名或未命名的 <c>DurableTask.RunInline</c>。</summary>
    public bool IsRunInline(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "RunInline"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, DurableTask);

    /// <summary>返回某调用是否创建委托定义：<c>DurableTask.Run</c>。</summary>
    public bool IsRun(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "Run"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, DurableTask);

    /// <summary>返回某调用是否为持久化操作命名：在定义或配置上调用 <c>WithId</c>。</summary>
    public static bool IsWithId(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "WithId" && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>
    /// 返回某调用是否配置定义或配置对象：<c>WithId</c>、<c>Detached</c> 或
    /// <c>Shielded</c>。
    /// </summary>
    public static bool IsConfiguration(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name is "WithId" or "Detached" or "Shielded"
        && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>返回某调用是否投递定义或配置对象。</summary>
    public static bool IsPost(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "Post" && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>
    /// 返回某调用是否轮询已调度任务：<c>IsCompletedAsync</c>、<c>GetStatusAsync</c> 或
    /// 带轮询选项的 <c>GetResponseAsync</c>。轮询观察其运行时刻的任务状态，不记录任何内容。
    /// </summary>
    public static bool IsPoll(IInvocationOperation invocation) =>
        IsDurableTasksType(invocation.TargetMethod.ContainingType)
        && invocation.TargetMethod.ContainingType.Name == "ScheduledTask"
        && (invocation.TargetMethod.Name is "IsCompletedAsync" or "GetStatusAsync"
            || (invocation.TargetMethod.Name == "GetResponseAsync"
                && invocation.TargetMethod.Parameters.Length > 0
                && invocation.TargetMethod.Parameters[0].Type.Name == "PollingOptions"));

    /// <summary>
    /// 返回某调用是否为 <c>DurableTask.RunInline</c> 效果无法运行的持久化操作：启动操作，
    /// 例如 <c>ScheduleAsync</c> 或 <c>Post</c>，或版本检查。嵌套的 <c>RunInline</c> 作为当前
    /// 效果的一部分运行。
    /// </summary>
    public bool IsOperationOutsideEffects(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return (IsPost(invocation) || (method.Name == "ScheduleAsync" && IsDurableTasksType(method.ContainingType)))
            || (method.Name == "GetVersion" && SymbolEqualityComparer.Default.Equals(method.ContainingType, DurableTask))
            || (method.Name is "Get" or "IsAtLeast" && SymbolEqualityComparer.Default.Equals(method.ContainingType, DurableVersion));
    }

    /// <summary>
    /// 返回某调用是否配置 await，使其延续操作不使用捕获的上下文，例如
    /// <c>ConfigureAwait(false)</c>，同时返回该实参的文本。
    /// </summary>
    public static bool ContinuesOffContext(IInvocationOperation invocation, out string argument)
    {
        argument = string.Empty;
        if (invocation.TargetMethod.Name != "ConfigureAwait" || invocation.Arguments.Length != 1)
        {
            return false;
        }

        var value = invocation.Arguments[0].Value;
        argument = value.Syntax.ToString();
        return value.ConstantValue is { HasValue: true, Value: var constant }
            && constant switch
            {
                bool continueOnCapturedContext => !continueOnCapturedContext,

                // ConfigureAwaitOptions.ContinueOnCapturedContext 的值为 1。
                int options when value.Type?.TypeKind == TypeKind.Enum => (options & 1) == 0,
                _ => false,
            };
    }

    /// <summary>返回某调用是否等待持久化信号。</summary>
    public bool IsSignalWait(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name is "WaitAsync" or "TryWaitAsync"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType?.OriginalDefinition, DurableSignal);

    /// <summary>返回某调用是否通过日志记录器记录日志。</summary>
    public bool IsLog(IInvocationOperation invocation, out IOperation? logger)
    {
        logger = null;
        var method = invocation.TargetMethod;
        if (Logger is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, LoggerExtensions)
            || (method.Name == "Log" && SymbolEqualityComparer.Default.Equals(method.ContainingType, Logger)))
        {
            logger = method.IsExtensionMethod || method.MethodKind == MethodKind.ReducedExtension
                ? GetExtensionReceiver(invocation)
                : invocation.Instance;
            return true;
        }

        if (LoggerMessageAttribute is not null && method.IsStatic && HasAttribute(method, LoggerMessageAttribute))
        {
            foreach (var argument in invocation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, Logger))
                {
                    logger = argument.Value;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>返回扩展方法调用的接收对象，或实例方法调用的实例。</summary>
    public static IOperation? GetExtensionReceiver(IInvocationOperation invocation) =>
        invocation.Instance ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

    /// <summary>
    /// 若声明块中没有其他代码再次为该局部变量赋值，则返回局部变量声明赋予它的值；
    /// 否则返回 null。
    /// </summary>
    public static IOperation? GetOnlyValue(ILocalSymbol local, SemanticModel semanticModel)
    {
        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax { Initializer: not null } declarator
            || semanticModel.GetOperation(declarator) is not IVariableDeclaratorOperation { Initializer.Value: { } value }
            || (declarator.FirstAncestorOrSelf<BlockSyntax>() is { } block
                && block.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                    assignment.Left is IdentifierNameSyntax { Identifier.ValueText: var name }
                    && name == local.Name
                    && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, local))))
        {
            return null;
        }

        return value;
    }

    /// <summary>从操作中去除转换及括号。</summary>
    public static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { IsImplicit: true } conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                default:
                    return operation;
            }
        }
    }

    private bool IsGrainStateDefinition(INamedTypeSymbol type)
    {
        foreach (var stateType in _grainStateTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, stateType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Implements(ITypeSymbol type, INamedTypeSymbol? definition)
    {
        if (definition is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition))
        {
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, definition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                return true;
            }
        }

        return false;
    }

    private static IPropertySymbol? GetProperty(INamedTypeSymbol? type, string name) =>
        type?.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();

    private static ImmutableArray<INamedTypeSymbol> Resolve(Compilation compilation, params string[] names)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>(names.Length);
        foreach (var name in names)
        {
            if (compilation.GetTypeByMetadataName(name) is { } type)
            {
                builder.Add(type);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>返回某符号是否为 <paramref name="types"/> 中的一员。</summary>
    public static bool IsOneOf(ITypeSymbol? type, ImmutableArray<INamedTypeSymbol> types)
    {
        foreach (var candidate in types)
        {
            if (SymbolEqualityComparer.Default.Equals(type?.OriginalDefinition, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回某类型中在不同运行之间产生不同值的成员。</summary>
    public static bool IsNonDeterministicMember(ISymbol member)
    {
        var type = member.ContainingType?.ToDisplayString();
        return (type, member.Name) switch
        {
            ("System.DateTime", "Now" or "UtcNow" or "Today") => true,
            ("System.DateTimeOffset", "Now" or "UtcNow") => true,
            ("System.Guid", "NewGuid" or "CreateVersion7") => true,
            ("System.Random", "Shared" or ".ctor") => true,
            ("System.Security.Cryptography.RandomNumberGenerator", _) => member.IsStatic,
            ("System.Diagnostics.Stopwatch", "StartNew" or "GetTimestamp" or ".ctor") => true,
            ("System.Environment", "TickCount" or "TickCount64" or "MachineName" or "ProcessId" or "CurrentManagedThreadId"
                or "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ProcessorCount") => true,
            ("System.TimeProvider", "GetUtcNow" or "GetLocalNow" or "GetTimestamp") => true,
            _ => false,
        };
    }

    /// <summary>返回用于在被调用方源码中标识持久化操作的方法名称。</summary>
    public static readonly ImmutableHashSet<string> DurableOperationNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "WithId",
        "RunInline",
        "Post",
        "ScheduleAsync",
        "SignalDurableTaskAsync",
        "TrySignalDurableTaskAsync",
        "WhenAll",
        "WhenAny",
        "WhenAllCompleted",
        "ScheduleAll",
        "WhenEach",
        "WaitAsync",
        "TryWaitAsync",
        "Delay",
        "DelayUntil");

    internal static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation)
    {
        yield return operation;
        foreach (var descendant in operation.Descendants())
        {
            yield return descendant;
        }
    }
}
