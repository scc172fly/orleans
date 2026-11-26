using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Orleans.Durable.Analyzers;

/// <summary>决心完成这一持久任务, grain 以及规则检查的平台符号, 按元数据名称 。</summary>
/// <remarks>匹配名称使分析师独立于抽象组装的版本。</remarks>
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

    /// <summary>获取 <c>DurableTaskContext.IsTaskCancellationRequested</c>,其中报告了持久任务取消的情况。</summary>
    public IPropertySymbol? IsTaskCancellationRequested { get; }

    /// <summary>
    /// 获取 <c>DurableTaskContext.CancellationToken</c>,任务取消和尝试取消都取消了。
    /// </summary>
    public IPropertySymbol? ContextCancellationToken { get; }

    public ImmutableArray<INamedTypeSymbol> NonDeterministicTypes { get; }

    /// <summary>返回引用耐久任务抽象或无效的汇编符号。</summary>
    public static DurableTaskSymbols? Create(Compilation compilation) =>
        compilation.GetTypeByMetadataName("Orleans.Durable.Tasks.DurableTask") is { } durableTask
            ? new DurableTaskSymbols(compilation, durableTask)
            : null;

    /// <summary>返回返回类型是否 <c>DurableTask</c> 或从它而生。</summary>
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

    /// <summary>返回是否在耐久任务抽象名称空间中声明了一种类型。</summary>
    public static bool IsDurableTasksType(ITypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() is { } name
        && (name == DurableTasksNamespace || name.StartsWith(DurableTasksNamespace + ".", StringComparison.Ordinal));

    /// <summary>
    /// 返回在耐久任务抽象取名称空间或其中的命名空间中是否声明某一类型,比较
    /// 命名空间符号, 不格式化其名称 。
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

    /// <summary>返回的方法是返回一个持久任务定义还是返回一个任务的配置。</summary>
    public bool ReturnsDefinition(IMethodSymbol method) => IsDefinitionType(method.ReturnType);

    /// <summary>返回一个类型是耐久任务定义还是一个配置。</summary>
    public bool IsDefinitionType(ITypeSymbol? type) =>
        IsDurableTask(type) || (IsDurableTasksType(type) && type!.Name == "ConfiguredDurableTask");

    /// <summary>返回返回返回是否一个类型或包含该类型的类型是 grain:它执行 <c>IGrainBase</c>.</summary>
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

    /// <summary>返回一种方法是否是一个持久任务机构:一种同步方法,它返回 <c>DurableTask</c>。</summary>
    public bool IsDurableBody(IMethodSymbol method) => method.IsAsync && IsDurableTask(method.ReturnType);

    /// <summary>
    /// 返回运行是否在持久任务机构内运行: <c>async DurableTask</c> 方法,或代表
    /// 耐久任务工厂,如 <c>DurableTask.Run</c>,运行,包括该机构所含的代码。
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

    /// <summary>返回接收代表作为论证的援引,或返回无效。</summary>
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
    /// 返回计算定义的操作,通过组合调用来包上它,以及是否
    /// 这些呼吁是 <paramref name="configuration"/>.
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
    /// 返回创建定义的操作,通过包装它的配置调用和是否
    /// 这些呼吁是 <paramref name="configuration"/>.
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
    /// 返回返回 是否返回 包含定义的组合调用 <c>Detached(rootId)</c>,其中提供
    /// 任务根点标识符,因此启动不需要执行的标识符。
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
    /// 返回返回,如果援引产生一个组合器,则返回在
    /// 所生成的操作标识符: <c>WhenAll</c>、 <c>WhenAny</c>、 <c>Race</c>、 <c>ScheduleAll</c>,
    /// <c>WhenAllCompleted</c>,定义 <c>WaitAsync</c> 或 <c>WaitUntilAsync</c>,或信号 <c>TryWaitAsync</c>。
    /// </summary>
    public bool IsCombinator(IInvocationOperation invocation) =>
        IsDurableTask(invocation.TargetMethod.ReturnType)
        && IsDurableTasksType(invocation.TargetMethod.ContainingType)
        && invocation.TargetMethod.Name is "WhenAll" or "WhenAny" or "Race" or "ScheduleAll" or "WhenAllCompleted" or "WaitAsync"
            or "WaitUntilAsync" or "TryWaitAsync"
        && !(invocation.TargetMethod.Name == "WaitAsync" && IsSignalWait(invocation));

    /// <summary>
    /// 返回一个操作是否读取 grain 状态持有的值: <c>IDurableValue&lt;T&gt;.Value</c>,
    /// <c>IPersistentState&lt;T&gt;.State</c>,或通过其索引器进行日记收藏的一个元素。
    /// </summary>
    public bool IsStateValue(IOperation operation) =>
        operation is IPropertyReferenceOperation { Instance.Type: { } stateType, Property: var property }
        && (property.IsIndexer
            ? IsGrainState(stateType)
            : (property.Name == "Value" && Implements(stateType, _durableValue))
                || (property.Name == "State" && Implements(stateType, _persistentState)));

    /// <summary>
    /// 返回某方法是否更改接收收藏的原样, 如 <c>List&lt;T&gt;.Add</c> 所做的那样 。
    /// 而不是像不可更改的收藏一样,返回已修改的副本。
    /// </summary>
    public static bool IsCollectionMutation(IMethodSymbol method) =>
        !method.IsStatic
        && method.Name is "Add" or "AddRange" or "Remove" or "RemoveAt" or "RemoveAll" or "RemoveRange" or "RemoveWhere" or "Clear"
            or "Insert" or "InsertRange" or "Enqueue" or "Dequeue" or "TryDequeue" or "Push" or "Pop" or "TryPop" or "TryAdd"
            or "TryRemove" or "UnionWith" or "IntersectWith" or "ExceptWith" or "SymmetricExceptWith" or "Sort" or "Reverse"
        && !SymbolEqualityComparer.Default.Equals(method.ReturnType.OriginalDefinition, method.ContainingType.OriginalDefinition)
        && method.ContainingNamespace?.ToDisplayString() != "System.Collections.Immutable";

    /// <summary>返回,无论类型是日记还是持续 grain 或执行其中的一种类型。</summary>
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
    /// 返回是否在等待期间观察运行任务的长期运作:等待的价值或等待者是
    /// 耐久任务类型,或生成的抽象 API 。
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

    /// <summary>返回是否出现工作效率: <c>DurableTask.RunInline</c>,命名或未命名。</summary>
    public bool IsRunInline(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "RunInline"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, DurableTask);

    /// <summary>返回援引是否创建代表定义: <c>DurableTask.Run</c>。</summary>
    public bool IsRun(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "Run"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, DurableTask);

    /// <summary>以下列方式返回: <c>WithId</c> 定义或配置。</summary>
    public static bool IsWithId(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "WithId" && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>
    /// 返回是否在引用中配置定义或配置: <c>WithId</c>、 <c>Detached</c>,或
    /// <c>Shielded</c>。
    /// </summary>
    public static bool IsConfiguration(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name is "WithId" or "Detached" or "Shielded"
        && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>返回是否在某一调换时设定定义或配置。</summary>
    public static bool IsPost(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "Post" && IsDurableTasksType(invocation.TargetMethod.ContainingType);

    /// <summary>
    /// 返回一次职业调查是否是预定任务: <c>IsCompletedAsync</c>, <c>GetStatusAsync</c>,或
    /// <c>GetResponseAsync</c> 民意测验在任务运行时观察任务状况,但没有任何记录。
    /// </summary>
    public static bool IsPoll(IInvocationOperation invocation) =>
        IsDurableTasksType(invocation.TargetMethod.ContainingType)
        && invocation.TargetMethod.ContainingType.Name == "ScheduledTask"
        && (invocation.TargetMethod.Name is "IsCompletedAsync" or "GetStatusAsync"
            || (invocation.TargetMethod.Name == "GetResponseAsync"
                && invocation.TargetMethod.Parameters.Length > 0
                && invocation.TargetMethod.Parameters[0].Type.Name == "PollingOptions"));

    /// <summary>
    /// 返回 <c>DurableTask.RunInline</c> 效果无法运行的援引是否为持久操作:开始
    /// 诸如 <c>ScheduleAsync</c> 或 <c>Post</c>或版本检查。 <c>RunInline</c> 以“一”为
    /// 效果。
    /// </summary>
    public bool IsOperationOutsideEffects(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return (IsPost(invocation) || (method.Name == "ScheduleAsync" && IsDurableTasksType(method.ContainingType)))
            || (method.Name == "GetVersion" && SymbolEqualityComparer.Default.Equals(method.ContainingType, DurableTask))
            || (method.Name is "Get" or "IsAtLeast" && SymbolEqualityComparer.Default.Equals(method.ContainingType, DurableVersion));
    }

    /// <summary>
    /// 返回是否在没有被捕获上下文的情况下, 以职业为由配置等待继续的等待
    /// <c>ConfigureAwait(false)</c>,并返回争论的文字。
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

                // ConfigureAwaitOptions.ContinueOnCapturedContext 是1。
                int options when value.Type?.TypeKind == TypeKind.Enum => (options & 1) == 0,
                _ => false,
            };
    }

    /// <summary>返回是否一个请求等待一个耐久信号。</summary>
    public bool IsSignalWait(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name is "WaitAsync" or "TryWaitAsync"
        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType?.OriginalDefinition, DurableSignal);

    /// <summary>返回是否通过日志对一个引用日志进行记录。</summary>
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

    /// <summary>返回扩展方法引用或实例援引的接收者。</summary>
    public static IOperation? GetExtensionReceiver(IInvocationOperation invocation) =>
        invocation.Instance ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);

    /// <summary>
    /// 返回本地语声明给它分配的值, 当声明区块中没有代码指定本地语时
    /// 或无效。
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

    /// <summary>操作的转换和括号。</summary>
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

    /// <summary>返回符号是否为 <paramref name="types"/>的符号。</summary>
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

    /// <summary>返回产生运行之间不同值的类型的成员。</summary>
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

    /// <summary>返回在被调用对象源中识别耐久操作的方法名称。</summary>
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
