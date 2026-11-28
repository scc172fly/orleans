using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Jobs;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>测试注入的基础设施故障。</summary>
/// <remarks>
/// 其消息以 <see cref="MessagePrefix"/> 开头，因此测试可以断言任何注入的故障都不会到达应用程序
/// 代码或任务记录的结果。
/// </remarks>
public sealed class InjectedFaultException(string message) : Exception(MessagePrefix + message)
{
    public const string MessagePrefix = "Injected fault: ";
}

/// <summary>故障注入的位置。</summary>
public enum FaultPoint
{
    /// <summary>日志写入在到达存储之前失败。</summary>
    JournalWriteBeforeDurable,

    /// <summary>日志写入到达存储，但其确认丢失：提交结果不确定。</summary>
    JournalWriteAfterDurable,

    /// <summary>日志读取失败，导致执行读取的激活失败。</summary>
    JournalRead,

    /// <summary>发件箱投递调用在到达接收方之前失败。</summary>
    DeliveryThrows,

    /// <summary>发件箱投递调用到达接收方，接收方处理该调用，但响应丢失。</summary>
    DeliveryResponseLost,

    /// <summary>发件箱投递调用一直不返回，直到测试释放该调用，随后调用超时。</summary>
    DeliveryHangs,

    /// <summary>注册在持久化任务接收处理器之前的接收处理器，在收件箱向其提供消息时抛出异常。</summary>
    InboxSinkThrows,

    /// <summary>注册在持久化任务消息处理器之前的消息处理器，在处理收件箱分派的消息时抛出异常。</summary>
    InboxHandlerThrows,

    /// <summary>Durable Jobs 对消息传递泵的唤醒在到达 Grain 之前失败。</summary>
    JobWakeupFails,

    /// <summary>发件箱投递调用返回或失败；仅计数，用于观察投递尝试。</summary>
    DeliveryCompleted,

    /// <summary>
    /// 接收方接纳投递调用，并在收件箱看到消息之前将其挂起，直至测试释放它。
    /// </summary>
    DeliveryHeldAtReceiver,

    /// <summary>
    /// 发件箱投递调用到达接收方，网络保留一份副本，测试稍后通过
    /// <see cref="InjectedFault.Duplicate"/> 再次投递该副本。
    /// </summary>
    DeliveryDuplicated,

    /// <summary>
    /// 激活实例在接纳持久化任务执行片段时请求自身停用，因此会在该执行片段的
    /// 代码运行期间停止。
    /// </summary>
    /// <remarks>
    /// 执行片段故障点对相同的操作计数，即目标 Grain 的各次激活所接纳的执行片段，因此
    /// 同一个出现索引在所有这些故障点上都指向同一个执行片段。
    /// </remarks>
    SegmentStartDeactivates,

    /// <summary>
    /// 持久化任务执行片段结束后，激活请求自身停用，因此激活在该执行片段对应的
    /// 执行尝试挂起于两个执行片段之间时，或在执行尝试结束后停止。
    /// </summary>
    SegmentEndDeactivates,

    /// <summary>
    /// 托管激活的 Silo 在激活接纳持久化任务执行片段时崩溃，发生在执行片段的
    /// 代码运行之前。
    /// </summary>
    SegmentStartCrashesSilo,

    /// <summary>
    /// 托管激活的 Silo 在持久化任务执行片段结束后崩溃，发生在激活接纳另一个
    /// 与该执行片段互斥的请求之前。
    /// </summary>
    SegmentEndCrashesSilo,

    /// <summary>
    /// 任务所在的 Grain 接纳对任务响应的轮询，并将其挂起而不予响应，直至测试释放该调用，模拟
    /// Silo 在轮询等待期间崩溃时留下的状态。
    /// </summary>
    PollHeldAtReceiver,
}

/// <summary>一个已预置的故障：它在第 <see cref="Occurrence"/> 次匹配操作时触发，从零开始计数。</summary>
public sealed class InjectedFault
{
    private readonly TaskCompletionSource _fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(int Count, TaskCompletionSource Completion)> _matchWaiters = [];
    private int _matches;

    internal InjectedFault(FaultPoint point, GrainId target, string? subject, int occurrence)
    {
        Point = point;
        Target = target;
        TargetKey = target.ToString();
        Subject = subject;
        Occurrence = occurrence;
    }

    public FaultPoint Point { get; }

    /// <summary>获取故障所针对的日志、收件箱或唤醒操作所属的 Grain；对于投递则为接收方。</summary>
    public GrainId Target { get; }

    /// <summary>获取故障所匹配的消息主题，若匹配所有消息则为 <see langword="null"/>。</summary>
    public string? Subject { get; }

    /// <summary>获取触发故障的匹配操作序号，或 <see cref="Every"/>。</summary>
    public int Occurrence { get; }

    /// <summary>在每次匹配操作时都触发的故障所使用的出现序号。</summary>
    public const int Every = -1;

    /// <summary>获取迄今匹配此故障的操作数量，包括触发故障的那一次。</summary>
    public int Matches
    {
        get
        {
            lock (_matchWaiters)
            {
                return _matches;
            }
        }
    }

    public bool HasFired => _fired.Task.IsCompleted;

    public Task Fired => _fired.Task;

    /// <summary>获取目标的 Grain ID 字符串，该字符串也是其日志 ID。</summary>
    internal string TargetKey { get; }

    internal Task Released => _released.Task;

    /// <summary>释放 <see cref="FaultPoint.DeliveryHangs"/> 故障所挂起的调用，随后该调用超时。</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>
    /// 获取 <see cref="FaultPoint.DeliveryDuplicated"/> 故障触发后，为其
    /// <see cref="Target"/> 保留的已投递消息副本。
    /// </summary>
    public InboxMessage? Duplicate { get; private set; }

    internal void KeepDuplicate(InboxMessage message) => Duplicate = message;

    /// <summary>返回一个任务，当有 <paramref name="count"/> 次操作匹配此故障时完成。</summary>
    public Task WaitForMatchesAsync(int count)
    {
        lock (_matchWaiters)
        {
            if (_matches >= count)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _matchWaiters.Add((count, completion));
            return completion.Task;
        }
    }

    /// <summary>对一次匹配操作计数，并返回该操作是否触发故障。</summary>
    internal bool OnMatch()
    {
        int matches;
        lock (_matchWaiters)
        {
            matches = ++_matches;
            foreach (var waiter in _matchWaiters.Where(waiter => waiter.Count <= matches).ToList())
            {
                _matchWaiters.Remove(waiter);
                waiter.Completion.TrySetResult();
            }
        }

        if (Occurrence != Every && matches - 1 != Occurrence)
        {
            return false;
        }

        _fired.TrySetResult();
        return true;
    }

    public override string ToString() => $"{Point} #{Occurrence} on {Target}{(Subject is null ? "" : $" ({Subject})")}";
}

/// <summary>
/// 向测试所针对的 Grain 的日志存储、发件箱投递、收件箱接收处理器和消息处理器、Durable Jobs 唤醒操作
/// 及持久化任务执行片段注入基础设施故障。
/// </summary>
/// <remarks>
/// 每个故障在计数达到指定操作时触发一次，因此测试通过出现
/// 索引枚举一次运行中的故障点。计数器是一种永不触发的故障。日志装饰器包装 Silo 的日志存储；
/// 传出 Grain 调用筛选器拦截发件箱投递和消息传递唤醒；传入 Grain 调用筛选器
/// 拦截投递以及运行持久化任务执行尝试的执行片段请求；Grain 则会在
/// 持久化任务运行时的接收处理器和消息处理器之前注册 <see cref="FaultInjectingInboxParticipant"/>。
/// </remarks>
public sealed class DurableTaskFaultInjector
{
    private readonly List<InjectedFault> _faults = [];

    /// <summary>
    /// 获取或设置指定地址处 Silo 的崩溃操作；执行片段崩溃故障在该 Silo 的调度器上启动此操作，
    /// 但不等待其完成。
    /// </summary>
    public Action<SiloAddress>? CrashSilo { get; set; }

    /// <summary>在 Silo 上注册注入器的日志存储装饰器、Grain 调用筛选器和服务。</summary>
    /// <remarks>在注册 Silo 的日志存储提供程序之后调用。</remarks>
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.Services.AddSingleton(this);
        var inner = siloBuilder.Services.Last(static descriptor => descriptor.ServiceType == typeof(IJournalStorageProvider));
        var innerFactory = inner.ImplementationFactory
            ?? throw new InvalidOperationException("The journal storage provider must be registered with a factory.");
        siloBuilder.Services.RemoveAll<IJournalStorageProvider>();
        siloBuilder.Services.AddSingleton<IJournalStorageProvider>(services =>
            new FaultInjectingJournalStorageProvider((IJournalStorageProvider)innerFactory(services), this));
        siloBuilder.AddOutgoingGrainCallFilter(new FaultInjectingCallFilter(this));
        siloBuilder.AddIncomingGrainCallFilter(new FaultInjectingReceiverFilter(this));
    }

    /// <summary>预置一个故障，使其在第 <paramref name="occurrence"/> 次匹配操作时触发，从零开始计数。</summary>
    public InjectedFault Arm(FaultPoint point, GrainId target, int occurrence, string? subject = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrence, InjectedFault.Every);
        return Add(new(point, target, subject, occurrence));
    }

    /// <summary>对 Grain 上某个故障点的操作计数，而不使任何操作失败。</summary>
    public InjectedFault Count(FaultPoint point, GrainId target, string? subject = null) =>
        Add(new(point, target, subject, int.MaxValue));

    /// <summary>移除所有预置故障和计数器，并释放挂起的调用。</summary>
    public void Reset()
    {
        lock (_faults)
        {
            foreach (var fault in _faults)
            {
                fault.Release();
            }

            _faults.Clear();
        }
    }

    /// <summary>对 <paramref name="targetKey"/> 上的一次操作计数，并返回针对此操作触发的故障（如果有）。</summary>
    internal InjectedFault? OnOperation(FaultPoint point, string targetKey, string? subject = null)
    {
        InjectedFault? firing = null;
        lock (_faults)
        {
            foreach (var fault in _faults)
            {
                if (fault.Point == point
                    && string.Equals(fault.TargetKey, targetKey, StringComparison.Ordinal)
                    && (fault.Subject is null || string.Equals(fault.Subject, subject, StringComparison.Ordinal))
                    && fault.OnMatch())
                {
                    firing ??= fault;
                }
            }
        }

        return firing;
    }

    private InjectedFault Add(InjectedFault fault)
    {
        lock (_faults)
        {
            _faults.Add(fault);
        }

        return fault;
    }

    private sealed class FaultInjectingJournalStorageProvider(IJournalStorageProvider inner, DurableTaskFaultInjector injector)
        : IJournalStorageProvider, IJournalStorageCatalog
    {
        public IJournalStorage CreateStorage(JournalId journalId) =>
            new FaultInjectingJournalStorage(inner.CreateStorage(journalId), journalId.Value, injector);

        public IAsyncEnumerable<JournalCatalogEntry> ListAsync(JournalCatalogListOptions? options = null, CancellationToken cancellationToken = default) =>
            ((IJournalStorageCatalog)inner).ListAsync(options, cancellationToken);
    }

    private sealed class FaultInjectingJournalStorage(IJournalStorage inner, string journalKey, DurableTaskFaultInjector injector)
        : IJournalStorage
    {
        public bool IsCompactionRequested => inner.IsCompactionRequested;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            if (injector.OnOperation(FaultPoint.JournalRead, journalKey) is { } fault)
            {
                throw new InjectedFaultException($"journal read failed ({fault}).");
            }

            return inner.ReadAsync(consumer, cancellationToken);
        }

        public ValueTask<bool> CreateIfNotExistsAsync(IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            inner.CreateIfNotExistsAsync(metadata, cancellationToken);

        public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default) =>
            inner.GetMetadataAsync(cancellationToken);

        public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
            IReadOnlyDictionary<string, string>? set = null,
            IEnumerable<string>? remove = null,
            string? expectedETag = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateMetadataAsync(set, remove, expectedETag, cancellationToken);

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) =>
            WriteAsync(static (storage, value, cancellationToken) => storage.ReplaceAsync(value, cancellationToken), value, cancellationToken);

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) =>
            WriteAsync(static (storage, value, cancellationToken) => storage.AppendAsync(value, cancellationToken), value, cancellationToken);

        public ValueTask DeleteAsync(CancellationToken cancellationToken) =>
            WriteAsync(static (storage, _, cancellationToken) => storage.DeleteAsync(cancellationToken), default, cancellationToken);

        private async ValueTask WriteAsync(
            Func<IJournalStorage, ReadOnlySequence<byte>, CancellationToken, ValueTask> write,
            ReadOnlySequence<byte> value,
            CancellationToken cancellationToken)
        {
            // 两种写入故障对相同的操作计数，因此同一个出现索引在任一模式下均指向同一次写入。
            var before = injector.OnOperation(FaultPoint.JournalWriteBeforeDurable, journalKey);
            var after = injector.OnOperation(FaultPoint.JournalWriteAfterDurable, journalKey);
            if (before is not null)
            {
                throw new InjectedFaultException($"journal write failed before it was durable ({before}).");
            }

            await write(inner, value, cancellationToken);
            if (after is not null)
            {
                throw new InjectedFaultException($"journal write was durable, and its acknowledgement was lost ({after}).");
            }
        }
    }

    private sealed class FaultInjectingCallFilter(DurableTaskFaultInjector injector) : IOutgoingGrainCallFilter
    {
        private const string MessagingPumpJobName = "orleans.messaging.pump";

        public Task Invoke(IOutgoingGrainCallContext context)
        {
            var declaringType = context.InterfaceMethod?.DeclaringType;
            if (declaringType == typeof(IMessagingExtension) && context.Request.GetArgument(0) is InboxMessage message)
            {
                return InvokeDeliveryAsync(context, message);
            }

            if (declaringType?.Name == "IDurableJobReceiverExtension"
                && context.Request.GetArgument(0) is IJobRunContext { Job.Name: MessagingPumpJobName }
                && injector.OnOperation(FaultPoint.JobWakeupFails, context.TargetId.ToString()) is { } wakeup)
            {
                return Task.FromException(new InjectedFaultException($"the messaging wake-up failed ({wakeup})."));
            }

            return context.Invoke();
        }

        private async Task InvokeDeliveryAsync(IOutgoingGrainCallContext context, InboxMessage message)
        {
            var target = context.TargetId.ToString();
            var subject = message.Envelope.Subject;
            var thrown = injector.OnOperation(FaultPoint.DeliveryThrows, target, subject);
            var lost = injector.OnOperation(FaultPoint.DeliveryResponseLost, target, subject);
            var hung = injector.OnOperation(FaultPoint.DeliveryHangs, target, subject);
            injector.OnOperation(FaultPoint.DeliveryDuplicated, target, subject)?.KeepDuplicate(message);
            if (thrown is not null)
            {
                throw new InjectedFaultException($"the delivery call failed ({thrown}).");
            }

            if (hung is not null)
            {
                var cancellationToken = context.Request.GetArgument(1) is CancellationToken token ? token : CancellationToken.None;
                await hung.Released.WaitAsync(cancellationToken);
                throw new TimeoutException($"{InjectedFaultException.MessagePrefix}the delivery call timed out ({hung}).");
            }

            try
            {
                await context.Invoke();
            }
            finally
            {
                injector.OnOperation(FaultPoint.DeliveryCompleted, target, subject);
            }

            if (lost is not null)
            {
                throw new InjectedFaultException($"the delivery response was lost after the receiver processed it ({lost}).");
            }
        }
    }
}

/// <summary>
/// 在接收方挂起已接纳的投递调用和轮询调用，并在持久化任务执行片段开始或结束时
/// 使激活停用或崩溃。
/// </summary>
/// <remarks>
/// 执行片段是接口为 <see cref="IDurableTaskSegment"/> 的本地请求，因此激活的传入调用
/// 筛选器可以像观察 Grain 调用一样观察它们，故障注入无需在运行时设置专用注入点。
/// </remarks>
internal sealed class FaultInjectingReceiverFilter(DurableTaskFaultInjector injector) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Request is IDurableTaskSegment)
        {
            await InvokeSegmentAsync(context);
            return;
        }

        if (context.InterfaceMethod?.DeclaringType == typeof(IMessagingExtension)
            && context.Request.GetArgument(0) is InboxMessage message
            && injector.OnOperation(FaultPoint.DeliveryHeldAtReceiver, context.TargetId.ToString(), message.Envelope.Subject) is { } hold)
        {
            await hold.Released;
        }

        if (context.MethodName == nameof(IDurableTaskServer.SubscribeOrPollAsync)
            && typeof(IDurableTaskServer).IsAssignableFrom(context.Request.GetInterfaceType())
            && injector.OnOperation(FaultPoint.PollHeldAtReceiver, context.TargetId.ToString()) is { } heldPoll)
        {
            await heldPoll.Released;
        }

        await context.Invoke();
    }

    private async Task InvokeSegmentAsync(IIncomingGrainCallContext context)
    {
        // 所有执行片段故障点都对每个执行片段计数，因此同一个出现索引在所有故障点上均指向同一个执行片段。
        var target = context.TargetId.ToString();
        var activation = context.TargetContext;
        var crashAtStart = injector.OnOperation(FaultPoint.SegmentStartCrashesSilo, target);
        var deactivateAtStart = injector.OnOperation(FaultPoint.SegmentStartDeactivates, target);
        var crashAtEnd = injector.OnOperation(FaultPoint.SegmentEndCrashesSilo, target);
        var deactivateAtEnd = injector.OnOperation(FaultPoint.SegmentEndDeactivates, target);
        if (crashAtStart is not null)
        {
            await CrashAsync(activation, crashAtStart);
        }

        if (deactivateAtStart is not null)
        {
            // 激活在其正在运行的请求（包括此执行片段）结束后停止。关闭过程会取消
            // 执行片段对应的执行尝试，其代码会继续运行，直至观察到取消。
            activation.Deactivate(new(DeactivationReasonCode.ApplicationRequested, InjectedFaultException.MessagePrefix + deactivateAtStart));
        }

        await context.Invoke();
        if (deactivateAtEnd is not null)
        {
            activation.Deactivate(new(DeactivationReasonCode.ApplicationRequested, InjectedFaultException.MessagePrefix + deactivateAtEnd));
        }

        if (crashAtEnd is not null)
        {
            await CrashAsync(activation, crashAtEnd);
        }
    }

    /// <summary>使托管激活的 Silo 崩溃，并挂起执行片段请求，该请求永远不会在此 Silo 上完成。</summary>
    private async Task CrashAsync(IGrainContext activation, InjectedFault fault)
    {
        var crash = injector.CrashSilo ?? throw new InvalidOperationException($"The harness doesn't crash silos ({fault}).");
        crash(activation.Address.SiloAddress!);
        await fault.Released;
        throw new InjectedFaultException($"the silo crashed ({fault}).");
    }
}

/// <summary>
/// Grain 在持久化任务运行时对应的处理器之前注册的接收处理器和消息处理器，使注入器能够使收件箱的
/// 消息提供或分派失败；否则两者均不接收该消息。
/// </summary>
/// <remarks>对于每次分派，收件箱在调用选定的消息处理器之前立即调用一次 <see cref="CanHandle"/>。</remarks>
public sealed class FaultInjectingInboxParticipant(DurableTaskFaultInjector injector, IGrainContext grainContext)
    : IInboxMessageSink, IInboxHandler
{
    private InjectedFault? _dispatchFault;

    public bool TryAccept(InboxMessage message, out DeliveryResult result)
    {
        result = default;
        if (injector.OnOperation(FaultPoint.InboxSinkThrows, grainContext.GrainId.ToString(), message.Envelope.Subject) is { } fault)
        {
            throw new InjectedFaultException($"the inbox sink failed ({fault}).");
        }

        return false;
    }

    public bool CanHandle(InboxMessage message)
    {
        _dispatchFault = injector.OnOperation(FaultPoint.InboxHandlerThrows, grainContext.GrainId.ToString(), message.Envelope.Subject);
        return _dispatchFault is not null;
    }

    public ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken) =>
        throw new InjectedFaultException($"the inbox handler failed ({_dispatchFault}).");
}
