using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>A grain的日记效果计数器及其短信空白信。</summary>
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
    /// 运行本地步、 <see cref="DurableTask.RunInline(string, Action)"/> 效果、呼叫显示
    /// 工作流程返回,信号等待,延迟, 和独立启动伙伴 它等待的。
    /// </summary>
    DurableTask<string> PipelineAsync(Guid partnerKey, string rootId);

    /// <summary>
    /// 与当地门对着等待着船口信号和延迟的车门进行比赛,所以车门对着车门
    /// 身体开始后会丢失,通过一个被保护的孩子进行补偿;然后用时间来计算信号等待。
    /// 取消中跑土地, 并进行补偿检查,
    /// </summary>
    DurableTask<string> RaceAsync(Guid partnerKey, string rootId);

    /// <summary>通知对方,然后通知对方 记录例外类型和讯息</summary>
    DurableTask<string> CatchPartnerFailureAsync(Guid partnerKey);

    /// <summary>号召伴侣,谁扔出,让例外逃脱。</summary>
    DurableTask<int> PropagatePartnerFailureAsync(Guid partnerKey);

    /// <summary>记录效果,然后抛出 <see cref="FaultApplicationException"/>。</summary>
    DurableTask<int> FailAsync(string message);

    /// <summary>从本地一步扔出 <see cref="FaultApplicationException"/> ,记录身体所观察到的。</summary>
    DurableTask<string> CatchLocalStepFailureAsync(string message);

    /// <summary>抛掷 <see cref="FaultApplicationException"/> a 发自 <see cref="DurableTask.RunInline(string, Action)"/> 效果。</summary>
    DurableTask<string> CatchOnceFailureAsync(string message);

    /// <summary>开始在根标识下对伙伴进行独立任务,并记录所观察到的启动。</summary>
    DurableTask<string> StartDetachedEchoAsync(Guid partnerKey, string detachedRootId, int value);

    /// <summary>等待着 <see cref="FaultWorkflowGrain.Ready"/> 信号</summary>
    DurableTask<int> WaitForReadyAsync();

    /// <summary>要求合伙人的门前一步</summary>
    DurableTask<int> CallGatedAsync(Guid partnerKey, int value);

    /// <summary>启动 <see cref="IFaultFencedGrain"/>上的独立回声,记录所观察到的启动。</summary>
    DurableTask<string> StartDetachedFencedEchoAsync(Guid fencedKey, string detachedRootId, int value);

    /// <summary>
    /// 运行根的一运行运行一次, 一旦保留被删除后再次运行 root 。 将 Run 的编号记录为
    /// 效果,调用伙伴的过程, 它的信号 <see cref="FaultWorkflowGrain.Ready"/> 后,等待该信号,
    /// 比赛合伙人的门前步 这场比赛与 <see cref="FaultWorkflowGrain.Go"/> 和延迟的定期的定期的定期的定期的定期的定期的定期的定期的
    /// <see cref="FaultWorkflowGrain.RunDeadline"/>.
    /// </summary>
    /// <remarks>
    /// 每一次运行都发出相同的请求和同样的信号值, 所以只有运行本身的化身 传达出它的信息
    /// 伴侣的步数等待门 <see cref="FaultWorkflowGrain.GetGateKey"/> 名称
    /// 跑步时
    /// </remarks>
    DurableTask<string> RunAgainAsync(Guid partnerKey, string rootId);
}

public interface IFaultPartnerGrain : IFaultInjectedGrain
{
    DurableTask<int> ProcessAsync(int value, Guid workflowKey, string rootId);

    DurableTask<int> EchoAsync(int value);

    /// <summary>回声像 <see cref="EchoAsync"/> 一样,通过另一种方法,因此任务与同一根ID下的回声相冲突。</summary>
    DurableTask<int> EchoAgainAsync(int value);

    DurableTask<string> HoldAsync(Guid workflowKey, string rootId);

    /// <summary>记录效果,然后抛出 <see cref="FaultApplicationException"/>。</summary>
    DurableTask<int> FailAsync(string message);

    /// <summary>信号 <see cref="FaultWorkflowGrain.Ready"/> 任务的任务 <see cref="IFaultFencedGrain"/>.</summary>
    DurableTask SignalFencedAsync(Guid fencedKey, string rootId, int value);

    /// <summary>运行一个等待伴侣的 <see cref="FaultTestGates"/> 大门的本地步。</summary>
    DurableTask<int> GatedStepAsync(int value);

    /// <summary>
    /// 运行一个本地步,等待门 <paramref name="gateKey"/>,返回 <paramref name="value"/> 加上
    /// 完成的门级数。
    /// </summary>
    DurableTask<int> StepThroughGateAsync(Guid gateKey, int value);
}

/// <summary>重新entrant 重试 grain 日记上测试围栏,但正在送交该杂志。</summary>
public interface IFaultFencedGrain : IFaultInjectedGrain
{
    DurableTask<int> EchoAsync(int value);

    /// <summary>呼叫伙伴的回声,此 grain的收件箱是否接受其完成 。</summary>
    DurableTask<int> CallEchoAsync(Guid partnerKey, int value);

    /// <summary>等待着 <see cref="FaultWorkflowGrain.Ready"/> 信号</summary>
    DurableTask<int> WaitForReadyAsync();

    /// <summary>阶段一个日记效果并写成,如果有的话,则返回该写作失败。</summary>
    Task<string> FenceAsync();
}

/// <summary>试验关口 grains 键键键,然后由 grain.</summary>
public sealed class FaultTestGates
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();

    public Task WaitAsync(Guid key, CancellationToken cancellationToken) => Get(key).Task.WaitAsync(cancellationToken);

    public void Open(Guid key) => Get(key).TrySetResult();

    private TaskCompletionSource Get(Guid key) =>
        _gates.GetOrAdd(key, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>申请失败,每个等待者、投票者和订阅者都认为没有问题。</summary>
[GenerateSerializer]
public sealed class FaultApplicationException : Exception
{
    public FaultApplicationException(string message) : base(message)
    {
    }
}

/// <summary>
/// 耐久 grain ,记录日记效应计数器并提前登记注射者参加方的收件箱
/// 耐久任务运行时间的水槽和处理者。
/// </summary>
/// <remarks>
/// grain 是在启动装置登记运行时间的汇和处理器之前建造的,因此参与者是第一个。
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

    /// <summary>添加一个已日记效果计数器;从 <see cref="DurableTask.RunInline(string, Func{int})"/> 效应中调用。</summary>
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

    /// <summary>获取每次赛跑 <see cref="RunAgainAsync"/> 的延迟时间, 远远超出赛程的长度。</summary>
    public static readonly TimeSpan RunDeadline = TimeSpan.FromHours(1);

    /// <summary>返回伙伴的步数等待运行工作流程时的 <paramref name="run"/> 关口。</summary>
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

        // 后期的耐久书写 将带来效果的记录, 因此重播会看到记录失败。
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
            // 工作流程可以在信号到达后立即取消搁置,这可在发送者完成之前完成。
            await Workflow(workflowKey).SignalDurableTaskAsync(TaskId.CreateRoot(rootId), FaultWorkflowGrain.Held, "held");
            stage = DurableTask.Current.IsTaskCancellationRequested ? "signaled" : "hold";
            await DurableTask.Run(static async cancellationToken => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))
                .WithId("hold");
            return "released";
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            // 第一次运行记录它观察到取消的阶段。重放记录它在同一阶段
            // 取消点,因此它从同一阶段进行补偿;一个在其他地方观察到的点,例如:
            // 开始,记录一个效果 情况并不会预期。
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
