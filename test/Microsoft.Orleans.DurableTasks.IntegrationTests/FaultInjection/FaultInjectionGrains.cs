using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>Grain 的日志化效果计数器及其消息传递死信。</summary>
[GenerateSerializer, Immutable]
public sealed class FaultGrainState
{
    [Id(0)]
    public required Dictionary<string, int> Effects { get; init; }

    [Id(1)]
    public required string[] InboxDeadLetters { get; init; }

    [Id(2)]
    public required string[] OutboxDeadLetters { get; init; }
}

public interface IFaultInjectedGrain : IGrainWithGuidKey
{
    Task<FaultGrainState> GetStateAsync();

    Task DeactivateAsync();
}

public interface IFaultWorkflowGrain : IFaultInjectedGrain
{
    /// <summary>
    /// 运行一个本地步骤、一个 <see cref="DurableTask.RunInline(string, Action)"/> 效果、一次向伙伴 Grain 发起且由其向
    /// 工作流发回信号的调用、一次信号等待、一次延迟，以及在伙伴 Grain 上启动并等待一个分离任务。
    /// </summary>
    DurableTask<string> PipelineAsync(Guid partnerKey, string rootId);

    /// <summary>
    /// 使对伙伴 Grain 挂起步骤的调用与一个等待该步骤信号和延迟的本地门控进行竞争，从而让挂起步骤
    /// 在其主体开始后落败并通过受取消保护的子任务进行补偿；随后令一次信号等待超时。挂起步骤的
    /// 取消在运行期间到达，其补偿会检查每次重放是否均在同一位置观察到取消。
    /// </summary>
    DurableTask<string> RaceAsync(Guid partnerKey, string rootId);

    /// <summary>调用伙伴 Grain（其会抛出异常），并记录异常的类型和消息。</summary>
    DurableTask<string> CatchPartnerFailureAsync(Guid partnerKey);

    /// <summary>调用伙伴 Grain（其会抛出异常），并让异常向外传播。</summary>
    DurableTask<int> PropagatePartnerFailureAsync(Guid partnerKey);

    /// <summary>记录一个效果，随后抛出 <see cref="FaultApplicationException"/>。</summary>
    DurableTask<int> FailAsync(string message);

    /// <summary>从本地步骤抛出 <see cref="FaultApplicationException"/>，并记录主体观察到的结果。</summary>
    DurableTask<string> CatchLocalStepFailureAsync(string message);

    /// <summary>抛出 <see cref="FaultApplicationException"/>，发生于 <see cref="DurableTask.RunInline(string, Action)"/> 效果中。</summary>
    DurableTask<string> CatchOnceFailureAsync(string message);

    /// <summary>在伙伴 Grain 上以根任务 ID 启动一个分离任务，并记录启动操作观察到的结果。</summary>
    DurableTask<string> StartDetachedEchoAsync(Guid partnerKey, string detachedRootId, int value);

    /// <summary>等待 <see cref="FaultWorkflowGrain.Ready"/> 信号。</summary>
    DurableTask<int> WaitForReadyAsync();

    /// <summary>调用伙伴 Grain 的门控步骤。</summary>
    DurableTask<int> CallGatedAsync(Guid partnerKey, int value);

    /// <summary>在 <see cref="IFaultFencedGrain"/> 上启动分离的回显任务，并记录启动操作观察到的结果。</summary>
    DurableTask<string> StartDetachedFencedEchoAsync(Guid fencedKey, string detachedRootId, int value);

    /// <summary>
    /// 执行一个根任务的一次运行（其先前运行在保留期结束被删除后可再次运行）：将运行序号记录为
    /// 效果，调用伙伴 Grain 的处理方法，后者发回 <see cref="FaultWorkflowGrain.Ready"/> 信号，等待该信号，
    /// 并使伙伴 Grain 此次运行的门控步骤与 <see cref="FaultWorkflowGrain.Go"/> 信号及一个延迟进行竞争，延迟时长为
    /// <see cref="FaultWorkflowGrain.RunDeadline"/>.
    /// </summary>
    /// <remarks>
    /// 每次运行发送相同的请求及相同的信号值，因此只有每次运行自身的运行世代标识才能将其消息
    /// 与先前运行的消息区分开来。伙伴 Grain 的步骤等待 <see cref="FaultWorkflowGrain.GetGateKey"/> 为
    /// 此次运行指定的门。
    /// </remarks>
    DurableTask<string> RunAgainAsync(Guid partnerKey, string rootId);
}

public interface IFaultPartnerGrain : IFaultInjectedGrain
{
    DurableTask<int> ProcessAsync(int value, Guid workflowKey, string rootId);

    DurableTask<int> EchoAsync(int value);

    /// <summary>像 <see cref="EchoAsync"/> 一样回显，但通过另一个方法执行，因此该任务与具有相同根任务 ID 的回显任务冲突。</summary>
    DurableTask<int> EchoAgainAsync(int value);

    DurableTask<string> HoldAsync(Guid workflowKey, string rootId);

    /// <summary>记录一个效果，随后抛出 <see cref="FaultApplicationException"/>。</summary>
    DurableTask<int> FailAsync(string message);

    /// <summary>将 <see cref="FaultWorkflowGrain.Ready"/> 信号发送给 <see cref="IFaultFencedGrain"/> 的任务。</summary>
    DurableTask SignalFencedAsync(Guid fencedKey, string rootId, int value);

    /// <summary>运行一个等待伙伴 Grain 的 <see cref="FaultTestGates"/> 门的本地步骤。</summary>
    DurableTask<int> GatedStepAsync(int value);

    /// <summary>
    /// 运行一个等待门 <paramref name="gateKey"/> 的本地步骤，并返回 <paramref name="value"/> 加上
    /// 已完成的门控步骤数量。
    /// </summary>
    DurableTask<int> StepThroughGateAsync(Guid gateKey, int value);
}

/// <summary>一个可重入的 Grain，测试会在向其投递消息期间隔离其日志。</summary>
public interface IFaultFencedGrain : IFaultInjectedGrain
{
    DurableTask<int> EchoAsync(int value);

    /// <summary>调用伙伴 Grain 的回显任务，该任务的完成消息由此 Grain 的收件箱接受。</summary>
    DurableTask<int> CallEchoAsync(Guid partnerKey, int value);

    /// <summary>等待 <see cref="FaultWorkflowGrain.Ready"/> 信号。</summary>
    DurableTask<int> WaitForReadyAsync();

    /// <summary>暂存一个日志化效果并将其写入，返回写入失败的结果（如果发生失败）。</summary>
    Task<string> FenceAsync();
}

/// <summary>测试 Grain 等待且由测试打开的门，以 Grain 为键。</summary>
public sealed class FaultTestGates
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();

    public Task WaitAsync(Guid key, CancellationToken cancellationToken) => Get(key).Task.WaitAsync(cancellationToken);

    public void Open(Guid key) => Get(key).TrySetResult();

    private TaskCompletionSource Get(Guid key) =>
        _gates.GetOrAdd(key, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>应用程序故障，每个等待者、轮询者和订阅者均原样观察到它。</summary>
[GenerateSerializer]
public sealed class FaultApplicationException : Exception
{
    public FaultApplicationException(string message) : base(message)
    {
    }
}

/// <summary>
/// 一个记录日志化效果计数器的持久化 Grain，并将注入器的收件箱参与者注册在
/// 持久化任务运行时的接收处理器和消息处理器之前。
/// </summary>
/// <remarks>
/// Grain 在激活设置注册运行时的接收处理器和消息处理器之前构造，因此该参与者排在首位。
/// </remarks>
public abstract class FaultInjectedGrain : DurableGrain, IFaultInjectedGrain
{
    private readonly IDurableDictionary<string, int> _effects;
    private readonly IMessagingDiagnostics _diagnostics;

    protected FaultInjectedGrain(
        IInbox inbox,
        IMessagingDiagnostics diagnostics,
        IDurableDictionary<string, int> effects,
        DurableTaskFaultInjector injector,
        IGrainContext grainContext)
    {
        var participant = new FaultInjectingInboxParticipant(injector, grainContext);
        inbox.Register((IInboxMessageSink)participant).Register((IInboxHandler)participant);
        _effects = effects;
        _diagnostics = diagnostics;
    }

    public Task<FaultGrainState> GetStateAsync() => Task.FromResult(new FaultGrainState
    {
        Effects = _effects.ToDictionary(),
        InboxDeadLetters = [.. _diagnostics.InboxDeadLetters.Select(static letter => $"{letter.Message.Envelope.MessageId}: {letter.Reason}")],
        OutboxDeadLetters = [.. _diagnostics.OutboxDeadLetters.Select(static letter => $"{letter.Message.Envelope.MessageId}: {letter.Reason}")],
    });

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    /// <summary>递增日志化效果计数器；从 <see cref="DurableTask.RunInline(string, Func{int})"/> 效果中调用此方法。</summary>
    protected int Increment(string effect)
    {
        var count = _effects.TryGetValue(effect, out var current) ? current + 1 : 1;
        _effects[effect] = count;
        return count;
    }
}

[GrainType("durable-task-fault-workflow-test")]
public sealed class FaultWorkflowGrain(
    IInbox inbox,
    IMessagingDiagnostics diagnostics,
    [FromKeyedServices("effects")] IDurableDictionary<string, int> effects,
    DurableTaskFaultInjector injector,
    IGrainContext grainContext)
    : FaultInjectedGrain(inbox, diagnostics, effects, injector, grainContext), IFaultWorkflowGrain
{
    public static readonly DurableSignal<int> Ready = new("ready");
    public static readonly DurableSignal<string> Held = new("held");
    public static readonly DurableSignal<string> Never = new("never");
    public static readonly DurableSignal<int> Go = new("go");

    /// <summary>获取 <see cref="RunAgainAsync"/> 每次运行参与竞争的延迟时长，远大于一次运行的时长。</summary>
    public static readonly TimeSpan RunDeadline = TimeSpan.FromHours(1);

    /// <summary>返回工作流第 <paramref name="run"/> 次运行中伙伴 Grain 的步骤所等待的门。</summary>
    public static Guid GetGateKey(Guid workflowKey, int run)
    {
        Span<byte> bytes = stackalloc byte[16];
        workflowKey.TryWriteBytes(bytes);
        bytes[15] ^= checked((byte)run);
        return new(bytes);
    }

    public async DurableTask<string> PipelineAsync(Guid partnerKey, string rootId)
    {
        var partner = Partner(partnerKey);
        var step = await DurableTask.Run(static _ => 20).WithId("step");
        var effect = DurableTask.RunInline("workflow", () => Increment("workflow"));
        var processed = await partner.ProcessAsync(step, this.GetPrimaryKey(), rootId).WithId("process");
        var ready = await Ready.WaitAsync();
        await DurableTask.Delay(DurableTaskFaultHarness.WorkflowDelay).WithId("pause");
        var echoed = await partner.EchoAsync(processed).Detached($"{rootId}-echo");
        return $"step={step};effect={effect};processed={processed};ready={ready};echoed={echoed}";
    }

    public async DurableTask<string> RaceAsync(Guid partnerKey, string rootId)
    {
        var (winner, outcome) = await DurableTask.Race([Partner(partnerKey).HoldAsync(this.GetPrimaryKey(), rootId), GateAsync()])
            .WithId("race");
        string deadline;
        try
        {
            deadline = await Never.WaitAsync().WaitAsync(DurableTaskFaultHarness.WorkflowDelay).WithId("deadline");
        }
        catch (TimeoutException)
        {
            deadline = "timed-out";
        }

        var effect = DurableTask.RunInline("race", () => Increment("race"));
        return $"winner={winner};outcome={outcome};deadline={deadline};effect={effect}";
    }

    public async DurableTask<string> CatchPartnerFailureAsync(Guid partnerKey)
    {
        try
        {
            await Partner(partnerKey).FailAsync("partner failed").WithId("fail");
            return "completed";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async DurableTask<int> PropagatePartnerFailureAsync(Guid partnerKey) =>
        await Partner(partnerKey).FailAsync("partner failed").WithId("fail");

    public async DurableTask<int> FailAsync(string message)
    {
        DurableTask.RunInline("fail", () => Increment("fail"));
        await DurableTask.Run(static _ => { }).WithId("before-failure");
        throw new FaultApplicationException(message);
    }

    public async DurableTask<string> CatchLocalStepFailureAsync(string message)
    {
        try
        {
            await DurableTask.Run(int (_) => throw new FaultApplicationException(message)).WithId("step");
            return "completed";
        }
        catch (Exception exception)
        {
            DurableTask.RunInline("caught", () => Increment("caught"));
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async DurableTask<string> CatchOnceFailureAsync(string message)
    {
        string observed;
        try
        {
            DurableTask.RunInline("throwing", int () =>
            {
                Increment("throwing");
                throw new FaultApplicationException(message);
            });
            observed = "completed";
        }
        catch (Exception exception)
        {
            observed = $"{exception.GetType().Name}: {exception.Message}";
        }

        // 后续的持久化写入提交该效果的记录，因此重放会观察到已记录的故障。
        await DurableTask.Run(static _ => { }).WithId("after");
        return observed;
    }

    public async DurableTask<string> StartDetachedEchoAsync(Guid partnerKey, string detachedRootId, int value)
    {
        try
        {
            return $"echoed {await Partner(partnerKey).EchoAsync(value).Detached(detachedRootId)}";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async DurableTask<int> WaitForReadyAsync() => await Ready.WaitAsync();

    public async DurableTask<int> CallGatedAsync(Guid partnerKey, int value) =>
        await Partner(partnerKey).GatedStepAsync(value).WithId("gated");

    public async DurableTask<string> StartDetachedFencedEchoAsync(Guid fencedKey, string detachedRootId, int value)
    {
        try
        {
            return $"echoed {await GrainFactory.GetGrain<IFaultFencedGrain>(fencedKey).EchoAsync(value).Detached(detachedRootId)}";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async DurableTask<string> RunAgainAsync(Guid partnerKey, string rootId)
    {
        var partner = Partner(partnerKey);
        var run = DurableTask.RunInline("run", () => Increment("run"));
        var processed = await partner.ProcessAsync(10, this.GetPrimaryKey(), rootId).WithId("process");
        var ready = await Ready.WaitAsync();
        var (winner, outcome) = await DurableTask.Race(
            [partner.StepThroughGateAsync(GetGateKey(this.GetPrimaryKey(), run), run), Go.WaitAsync(), ExpireAsync()])
            .WithId("race");
        return $"run={run};processed={processed};ready={ready};winner={winner};outcome={outcome}";
    }

    private static async DurableTask<int> ExpireAsync()
    {
        await DurableTask.Delay(RunDeadline).WithId("deadline");
        return -1;
    }

    private static async DurableTask<string> GateAsync()
    {
        var held = await Held.WaitAsync();
        await DurableTask.Delay(DurableTaskFaultHarness.WorkflowDelay);
        return $"gate after {held}";
    }

    private IFaultPartnerGrain Partner(Guid key) => GrainFactory.GetGrain<IFaultPartnerGrain>(key);
}

[GrainType("durable-task-fault-partner-test")]
public sealed class FaultPartnerGrain(
    IInbox inbox,
    IMessagingDiagnostics diagnostics,
    [FromKeyedServices("effects")] IDurableDictionary<string, int> effects,
    DurableTaskFaultInjector injector,
    IGrainContext grainContext,
    FaultTestGates gates,
    DurableTaskMessagingProbe probe)
    : FaultInjectedGrain(inbox, diagnostics, effects, injector, grainContext), IFaultPartnerGrain
{
    public DurableTask<int> GatedStepAsync(int value) => StepThroughGateAsync(this.GetPrimaryKey(), value);

    public async DurableTask<int> StepThroughGateAsync(Guid gateKey, int value) =>
        await DurableTask.Run(async cancellationToken =>
        {
            probe.RecordStarted(gateKey);
            await gates.WaitAsync(gateKey, cancellationToken);
            return value + DurableTask.RunInline("gated", () => Increment("gated"));
        }).WithId("gated");


    public async DurableTask<int> ProcessAsync(int value, Guid workflowKey, string rootId)
    {
        var effect = DurableTask.RunInline("process", () => Increment("process"));
        await Workflow(workflowKey).SignalDurableTaskAsync(TaskId.CreateRoot(rootId), FaultWorkflowGrain.Ready, value * 3);
        return (value * 2) + effect - 1;
    }

    public DurableTask<int> EchoAsync(int value) =>
        DurableTask.Run(_ => value + DurableTask.RunInline("echo", () => Increment("echo")));

    public DurableTask<int> EchoAgainAsync(int value) => EchoAsync(value);

    public async DurableTask<string> HoldAsync(Guid workflowKey, string rootId)
    {
        var stage = "send";
        try
        {
            // 工作流可以在信号一到达时就取消挂起步骤，此时发送操作本身可能尚未完成。
            await Workflow(workflowKey).SignalDurableTaskAsync(TaskId.CreateRoot(rootId), FaultWorkflowGrain.Held, "held");
            stage = DurableTask.Current.IsTaskCancellationRequested ? "signaled" : "hold";
            await DurableTask.Run(static async cancellationToken => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))
                .WithId("hold");
            return "released";
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 首次运行会记录其观察到取消的阶段。重放在相同的
            // 取消点观察到取消，因此从相同的阶段进行补偿；若在其他位置（例如运行
            // 开始时）观察到取消，则会记录场景所不期望的效果。
            if (DurableTask.RunInline("observed", () => stage) != stage)
            {
                DurableTask.RunInline("observed-elsewhere", () => Increment("observed-elsewhere"));
            }

            var compensation = await DurableTask.Run(_ => DurableTask.RunInline("compensate", () => Increment("compensate")))
                .WithId("compensate")
                .Shielded();
            return $"compensated {compensation}";
        }
    }

    public async DurableTask<int> FailAsync(string message)
    {
        DurableTask.RunInline("fail", () => Increment("fail"));
        await DurableTask.Run(static _ => { }).WithId("before-failure");
        throw new FaultApplicationException(message);
    }

    public async DurableTask SignalFencedAsync(Guid fencedKey, string rootId, int value) =>
        await GrainFactory.GetGrain<IFaultFencedGrain>(fencedKey).SignalDurableTaskAsync(TaskId.CreateRoot(rootId), FaultWorkflowGrain.Ready, value);

    private IFaultWorkflowGrain Workflow(Guid key) => GrainFactory.GetGrain<IFaultWorkflowGrain>(key);
}

[Reentrant]
[GrainType("durable-task-fault-fenced-test")]
public sealed class FaultFencedGrain(
    IInbox inbox,
    IMessagingDiagnostics diagnostics,
    [FromKeyedServices("effects")] IDurableDictionary<string, int> effects,
    DurableTaskFaultInjector injector,
    IGrainContext grainContext)
    : FaultInjectedGrain(inbox, diagnostics, effects, injector, grainContext), IFaultFencedGrain
{
    public DurableTask<int> EchoAsync(int value) =>
        DurableTask.Run(_ => value + DurableTask.RunInline("echo", () => Increment("echo")));

    public async DurableTask<int> CallEchoAsync(Guid partnerKey, int value) =>
        await GrainFactory.GetGrain<IFaultPartnerGrain>(partnerKey).EchoAsync(value).WithId("echo");

    public async DurableTask<int> WaitForReadyAsync() => await FaultWorkflowGrain.Ready.WaitAsync();

    public async Task<string> FenceAsync()
    {
        Increment("fence");
        string outcome;
        try
        {
            await WriteStateAsync();
            outcome = "written";
        }
        catch (Exception exception)
        {
            outcome = exception.Message;
        }

        return outcome;
    }
}
