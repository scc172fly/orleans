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
/// 其信件以 <see cref="MessagePrefix"/>开头, 因此测试可以断言没有注入的失败会到达应用程序
/// 或任务记录的结果。
/// </remarks>
public sealed class InjectedFaultException(string message) : Exception(MessagePrefix + message)
{
    public const string MessagePrefix = "Injected fault: ";
}

/// <summary>注射过错的地方。</summary>
public enum FaultPoint
{
    /// <summary>日记在到达存储前写失败。</summary>
    JournalWriteBeforeDurable,

    /// <summary>日记写到存储处,</summary>
    JournalWriteAfterDurable,

    /// <summary>日记读失败, 无法启动读取它。</summary>
    JournalRead,

    /// <summary>发箱送货通知在到达接收器之前失败了</summary>
    DeliveryThrows,

    /// <summary>发箱发送电话到达接收者,由接收者处理,其响应丢失。</summary>
    DeliveryResponseLost,

    /// <summary>发箱送货电话在测试释放之前不会回复 然后重复</summary>
    DeliveryHangs,

    /// <summary>在一个持久任务汇之前登记的水槽在信箱发出信息时会投下。</summary>
    InboxSinkThrows,

    /// <summary>在一个持久任务处理器前登记的操作员会将信箱发送的信息丢在一个信息上。</summary>
    InboxHandlerThrows,

    /// <summary>信息泵在到达之前就失败了, grain.</summary>
    JobWakeupFails,

    /// <summary>发件箱发送电话返回或失败;仅计算,以观察发送尝试。</summary>
    DeliveryCompleted,

    /// <summary>
    /// 接收器承认有送货电话,并在收件箱看到信息之前持有它,直到测试释放。
    /// </summary>
    DeliveryHeldAtReceiver,

    /// <summary>
    /// 发箱送货电话到达接收机,网络保存一份副本,测试再次发出
    /// 后附 <see cref="InjectedFault.Duplicate"/>。
    /// </summary>
    DeliveryDuplicated,

    /// <summary>
    /// 启动程序本身要求解除其自有的功能,因为它承认了一个耐用的任务段,因此在启动程序开始之前停止。
    /// 代码运行。
    /// </summary>
    /// <remarks>
    /// 点点段数计算相同的操作,目标 grain 启动时所接受的区段,因此
    /// 每个区域都出现索引相同段的名称。
    /// </remarks>
    SegmentStartDeactivates,

    /// <summary>
    /// 一旦一个持久任务段结束,启动程序要求它自己停止运行,因此在该段结束时停止运行。
    /// 尝试在段间或尝试结束后停放。
    /// </summary>
    SegmentEndDeactivates,

    /// <summary>
    /// 下 silo 激活启动之前, 当启动允许一个持久任务段时, 即启动启动崩溃时主机所在的点 。
    /// 代码运行。
    /// </summary>
    SegmentStartCrashesSilo,

    /// <summary>
    /// 一旦耐久任务段结束, 启动允许另一个任务段之前的启动崩溃时主机的 silo
    /// 中不包括该段的请求。
    /// </summary>
    SegmentEndCrashesSilo,

    /// <summary>
    /// 任务 grain 接受一项调查,以回应任务的反应,并坚持这项调查,在试验释放前未予答复,作为
    /// 投票等待时撞车的silo
    /// </summary>
    PollHeldAtReceiver,
}

/// <summary>一次武装过错: <see cref="Occurrence"/>与它匹配的操作 从零开始计数</summary>
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

    /// <summary>获取日记、收件箱或提醒故障目标的 grain ;交付时,获取接收器。</summary>
    public GrainId Target { get; }

    /// <summary>获取错误匹配的电文主题,或每个电文的 <see langword="null"/> 。</summary>
    public string? Subject { get; }

    /// <summary>获得发生故障或 <see cref="Every"/>时的匹配操作。</summary>
    public int Occurrence { get; }

    /// <summary>每次匹配操作发生火灾的错误。</summary>
    public const int Every = -1;

    /// <summary>获得迄今为止与错误相符的行动数量,包括发射行动。</summary>
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

    /// <summary>获取目标的 grain 身份字符串, 也就是它的日记 ID。</summary>
    internal string TargetKey { get; }

    internal Task Released => _released.Task;

    /// <summary>释放出一个 <see cref="FaultPoint.DeliveryHangs"/> 错误的调用,然后断开。</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>
    /// 获取发送的信件副本,该信件因 <see cref="FaultPoint.DeliveryDuplicated"/> 错误而保存
    /// <see cref="Target"/>,一旦 故障解雇。
    /// </summary>
    public InboxMessage? Duplicate { get; private set; }

    internal void KeepDuplicate(InboxMessage message) => Duplicate = message;

    /// <summary>返回一个任务,该任务一旦 <paramref name="count"/> 操作与错误匹配即完成。</summary>
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

    /// <summary>计数匹配操作并返回是否发生故障。</summary>
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
/// 将基础设施的缺陷注入期刊储存、发箱交付、收箱汇和处理器、持久工作提醒、
/// 以及试验目标 grains 的耐久任务部分。
/// </summary>
/// <remarks>
/// 每个故障一次起火一次, 以其计算操作的功率, 所以试验会列出一个由它们运行的错误点
/// 发生索引。计数器是一个从未火灾的错误。日记设计师将 silo日记存储、
/// 发送 grain 调用过滤器拦截发箱发送和消息提醒, 即将到来的 grain 呼叫过滤器
/// 截截交付和进行持久任务尝试的段段请求, grains 登记
/// <see cref="FaultInjectingInboxParticipant"/> 时间轴的下水道和操作器
/// </remarks>
public sealed class DurableTaskFaultInjector
{
    private readonly List<InjectedFault> _faults = [];

    /// <summary>
    /// 获取或设置 silo 地址的崩溃,该地址是 silo调度器和
    /// 等不及了
    /// </summary>
    public Action<SiloAddress>? CrashSilo { get; set; }

    /// <summary>注册喷射器日记存储设计师 grain 调用过滤器,以及 A silo.</summary>
    /// <remarks>在 silo日记存储提供者注册后调用。</remarks>
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

    /// <summary>武器有错,在 <paramref name="occurrence"/>第匹配操作上开火,从零计。</summary>
    public InjectedFault Arm(FaultPoint point, GrainId target, int occurrence, string? subject = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrence, InjectedFault.Every);
        return Add(new(point, target, subject, occurrence));
    }

    /// <summary>计算一个错误点对 a grain,没有失败 他们中的任何一个。</summary>
    public InjectedFault Count(FaultPoint point, GrainId target, string? subject = null) =>
        Add(new(point, target, subject, int.MaxValue));

    /// <summary>清除所有武装过失和反弹,并发布挂号电话。</summary>
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

    /// <summary>计数 <paramref name="targetKey"/> 的操作,并返回为之开火的过错。</summary>
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
            // 两种写错都计算相同的操作, 所以发生时索引以任一模式命名相同写入 。
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
/// 控制接收器接住接收器的接送电话和民意测验,在启动或结束时停止使用或碰撞激活
/// 长期任务部分。
/// </summary>
/// <remarks>
/// 片段是本地请求,其界面为 <see cref="IDurableTaskSegment"/>,因此激活的呼叫
/// 过滤器在观察时会加以观察 grain 电话响了,故障不需要在运行时接合
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
        // 每个段点数每个段数, 所以发生时索引在每段点上都标出相同的段数 。
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
            // 一旦启动程序请求即停止, 包括此段的激活结束。 它的关闭取消
            // 选择一个部分的尝试,其代码运行到它看到取消。
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

    /// <summary>崩溃激活的 silo, 并保留部分的请求, 请求从未完成 。</summary>
    private async Task CrashAsync(IGrainContext activation, InjectedFault fault)
    {
        var crash = injector.CrashSilo ?? throw new InvalidOperationException($"The harness doesn't crash silos ({fault}).");
        crash(activation.Address.SiloAddress!);
        await fault.Released;
        throw new InjectedFaultException($"the silo crashed ({fault}).");
    }
}

/// <summary>
/// i grain 在持久任务运行时间之前登记的一个水槽和处理器,以便输入器能使收件箱失效
/// 发出或发件电文;否则,两者均拒绝。
/// </summary>
/// <remarks>收件箱呼叫 <see cref="CanHandle"/> 每一次,在被选的处理者面前。</remarks>
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
