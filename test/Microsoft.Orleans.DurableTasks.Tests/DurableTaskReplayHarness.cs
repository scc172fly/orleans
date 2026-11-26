#nullable enable
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Tasks.Storage;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 在一个日记上主持耐久任务运行时间和 grain的日记状态,因此测试观察每份日记的内容
/// 仅从承诺日记上进行并收回每次激活。
/// </summary>
/// <remarks>
/// 停止激活的终止时间停止运行时没有进一步的写作, 以崩溃或停用的方式停止运行, 所以请说明
/// 未写入已执行的写作已丢失。 下一次激活将恢复日记并重播未完成的工作流程 。
/// </remarks>
internal sealed class DurableTaskReplayHarness(
    Func<ReplayGrain, DurableTask> workflow,
    string journalFormatKey = DurableTaskJournalHarness.OrleansBinary) : IAsyncDisposable
{
    public static readonly GrainId GrainId = GrainId.Create("replay", "one");

    private readonly DurableTaskJournalHarness _journal = new(journalFormatKey);
    private int _effectRuns;

    /// <summary>获取或设置每个激活运行的工作流程,例如部署后较晚版本。</summary>
    public Func<ReplayGrain, DurableTask> Workflow { get; set; } = workflow;

    /// <summary>获得每个激活运行时间的选项。</summary>
    public DurableTaskOptions Options { get; } = new() { ResultRetentionPeriod = TimeSpan.FromHours(1) };

    /// <summary>得到每次激活都有多少次效果运行。</summary>
    public int EffectRuns => Volatile.Read(ref _effectRuns);

    /// <summary>计为效果的一行。</summary>
    public void CountEffectRun() => Interlocked.Increment(ref _effectRuns);

    /// <summary>创建激活,并从承诺日记本上恢复其状态和待处理的工作流程。</summary>
    public async Task<ReplayActivation> ActivateAsync(CancellationToken cancellationToken)
    {
        var scope = _journal.CreateScope();
        try
        {
            var services = scope.ServiceProvider;
            var storage = services.GetRequiredService<DurableTaskGrainStorage>();
            var logicalTimes = services.GetRequiredKeyedService<IDurableDictionary<TaskId, DateTimeOffset>>("$task-logical-times");
            var invocations = services.GetRequiredKeyedService<IDurableDictionary<MessageId, PendingInvocation>>("$task-invocations");
            var counter = services.GetRequiredKeyedService<IDurableValue<int>>("counter");
            var log = services.GetRequiredKeyedService<IDurableLog<int>>("log");
            var manager = services.GetRequiredService<IJournaledStateManager>();
            var grain = new ReplayGrain(this, counter, log, manager);
            var grainContext = Substitute.For<IGrainContext>();
            grainContext.GrainId.Returns(GrainId);
            grainContext.GetTarget().Returns(grain);
            var shared = new DurableTaskGrainRuntimeShared(
                new TestGrainContextAccessor(grainContext),
                TimeProvider.System,
                NullLogger<DurableTaskGrainRuntime>.Instance,
                Microsoft.Extensions.Options.Options.Create(Options),
                Microsoft.Extensions.Options.Options.Create(new DurableMessagingOptions()),
                _journal.Serializer);
            var runtime = new DurableTaskGrainRuntime(storage, logicalTimes, invocations, shared, [new RecordingDurableTaskMessageTransport()]);
            var probe = new WriteProbe(() => (counter.Value, log.Count, CompletedTasks(storage)));
            manager.RegisterStateMachine(DurableTaskGrainRuntime.StateMachineName, runtime);
            manager.RegisterStateMachine("write-probe", probe);
            TestLocalRequestQueue.Attach(grainContext);
            runtime.InitializeForActivation(grainContext);
            await storage.ReadAsync(cancellationToken);
            await runtime.RecoverAsync(cancellationToken);
            runtime.StartRecoveredExecutions();
            return new(scope, runtime, storage, grain, probe);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _journal.DisposeAsync();

    private static TaskId[] CompletedTasks(DurableTaskGrainStorage storage) =>
        [.. storage.Tasks.Where(static task => task.State.CompletedAt.HasValue).Select(static task => task.Id)];
}

/// <summary>启动 grain 减去 <see cref="DurableTaskReplayHarness"/> 主机主机。</summary>
internal sealed class ReplayActivation(
    AsyncServiceScope scope,
    DurableTaskGrainRuntime runtime,
    DurableTaskGrainStorage storage,
    ReplayGrain grain,
    WriteProbe probe) : IAsyncDisposable
{
    public DurableTaskGrainRuntime Runtime { get; } = runtime;

    public DurableTaskGrainStorage Storage { get; } = storage;

    public ReplayGrain Grain { get; } = grain;

    /// <summary>获取 grain计数器、其日志条目计数和每个日记所写已完成的任务。</summary>
    public IReadOnlyCollection<(int Counter, int Entries, TaskId[] Completed)> Writes => probe.Writes;

    /// <summary>完成后一旦写失败的完整 <see cref="FailWrites"/>.</summary>
    public Task FailedWrite => probe.FailedWrite;

    /// <summary>
    /// 将以后的日记在到达存储前写不成功, 所以从现在开始启动阶段的变化
    /// 当它灭亡的时候,他们迷失了;当它灭亡的时候,他们却像在它们灭亡的时候 silo 写作前坠机。
    /// </summary>
    public void FailWrites() => probe.FailWrites();

    /// <summary>将工具的工作流程与客户端一样排入时间表。</summary>
    public async Task ScheduleAsync(TaskId taskId, CancellationToken cancellationToken) =>
        await ((IDurableTaskServer)Runtime).ScheduleAsync(
            taskId,
            new ReplayWorkflowRequest
            {
                Context = new DurableTaskRequestContext { TargetId = DurableTaskReplayHarness.GrainId },
            },
            cancellationToken);

    public async Task<DurableTaskResponse> GetResponseAsync(TaskId taskId, CancellationToken cancellationToken) =>
        await ((IDurableTaskManager)Runtime).GetTask(taskId).GetResponseAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    /// <summary>不再写日记, 即终止激活。</summary>
    public async ValueTask DisposeAsync()
    {
        await Runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await scope.DisposeAsync();
    }
}

/// <summary>下 grain 一种激活实例:其日记计数和日志,以及工作流程的控制。</summary>
internal sealed class ReplayGrain(
    DurableTaskReplayHarness harness,
    IDurableValue<int> counter,
    IDurableLog<int> log,
    IJournaledStateManager manager)
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _atGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Task> _unrelatedWrites = new();

    public DurableTaskReplayHarness Harness { get; } = harness;

    /// <summary>获取 grain日记状态。</summary>
    public IDurableValue<int> Counter { get; } = counter;

    /// <summary>获取 grain日志,工作流程发布其进度。</summary>
    public IDurableLog<int> Log { get; } = log;

    /// <summary>一旦工作流程在大门等候,完成。</summary>
    public Task AtGate => _atGate.Task;

    /// <summary>等待测试打开大门,或尝试中止。</summary>
    public Task WaitAtGateAsync()
    {
        _atGate.TrySetResult();
        return _gate.Task.WaitAsync(DurableTask.Current.CancellationToken);
    }

    public void OpenGate() => _gate.TrySetResult();

    /// <summary>要求一个日记像其他 grain 工作一样,记录运行时所演的任何戏。</summary>
    public void RequestUnrelatedWrite() => _unrelatedWrites.Enqueue(manager.WriteStateAsync().AsTask());

    /// <summary>等待工作流程所要求的不相干写作。</summary>
    public Task UnrelatedWrites => Task.WhenAll(_unrelatedWrites);

    public DurableTask CreateWorkflow() => Harness.Workflow(this);
}

/// <summary>一份日记国家机器, 什么都不写,记录每个写着收集的东西,</summary>
internal sealed class WriteProbe(Func<(int Counter, int Entries, TaskId[] Completed)> snapshot) : IStateMachine
{
    private readonly ConcurrentQueue<(int Counter, int Entries, TaskId[] Completed)> _writes = new();
    private readonly TaskCompletionSource _failedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _failsWrites;

    public IReadOnlyCollection<(int Counter, int Entries, TaskId[] Completed)> Writes => _writes;

    /// <summary>一旦探测器写不出来, 就会完成 。</summary>
    public Task FailedWrite => _failedWrite.Task;

    /// <summary>无法编写每以后的书稿,因此以后不会写到储存处。</summary>
    public void FailWrites() => _failsWrites = true;

    ValueTask IStateMachine.PrepareWriteAsync(CancellationToken cancellationToken)
    {
        if (!_failsWrites)
        {
            return ValueTask.CompletedTask;
        }

        _failedWrite.TrySetResult();
        return ValueTask.FromException(new InvalidOperationException("The write probe failed the write before it reached storage."));
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        throw new InvalidOperationException("The write probe writes no entries.");

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => _writes.Enqueue(snapshot());

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer) => _writes.Enqueue(snapshot());
}

/// <summary>一份可连号请求,用于运行启动工作流程,该程序的目标。</summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.ReplayWorkflowRequest")]
internal sealed class ReplayWorkflowRequest : IDurableTaskRequest
{
    [NonSerialized]
    private ReplayGrain? _grain;

    [Id(0)]
    public DurableTaskRequestContext? Context { get; set; }

    [Id(1)]
    public InvokeMethodOptions Options { get; private set; }

    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;

    public object? GetTarget() => _grain;

    public void SetTarget(ITargetHolder holder) => _grain = (ReplayGrain?)holder.GetTarget();

    public ValueTask<Response> Invoke() => throw new NotSupportedException("Durable task requests are not invoked directly.");

    public int GetArgumentCount() => 0;

    public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(nameof(index));

    public string GetMethodName() => "Run";

    public string GetInterfaceName() => "Microsoft.Orleans.DurableTasks.Tests.IReplayGrain";

    public string GetActivityName() => "Microsoft.Orleans.DurableTasks.Tests.IReplayGrain/Run";

    public MethodInfo GetMethod() => typeof(ReplayWorkflowRequest).GetMethod(nameof(GetMethod))!;

    public Type GetInterfaceType() => typeof(ReplayWorkflowRequest);

    public void Dispose()
    {
    }

    public DurableTask CreateTask() => (_grain ?? throw new InvalidOperationException("The request has no target.")).CreateWorkflow();
}
