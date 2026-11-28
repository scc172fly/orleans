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
/// 在同一日志上承载持久化任务运行时及 Grain 的日志化状态，使测试能够观察每次日志
/// 写入提交的内容，并仅从已提交的日志恢复各激活实例。
/// </summary>
/// <remarks>
/// 结束激活实例会停止其运行时而不再执行写入，与崩溃或停用时相同，因此
/// 未经任何写入提交的状态都会丢失。下一个激活实例恢复日志并重放待处理工作流。
/// </remarks>
internal sealed class DurableTaskReplayHarness(
    Func<ReplayGrain, DurableTask> workflow,
    string journalFormatKey = DurableTaskJournalHarness.OrleansBinary) : IAsyncDisposable
{
    public static readonly GrainId GrainId = GrainId.Create("replay", "one");

    private readonly DurableTaskJournalHarness _journal = new(journalFormatKey);
    private int _effectRuns;

    /// <summary>获取或设置每个激活实例运行的工作流，例如部署后的较新版本。</summary>
    public Func<ReplayGrain, DurableTask> Workflow { get; set; } = workflow;

    /// <summary>获取每个激活实例的运行时所读取的选项。</summary>
    public DurableTaskOptions Options { get; } = new() { ResultRetentionPeriod = TimeSpan.FromHours(1) };

    /// <summary>获取跨所有激活实例运行效果的总次数。</summary>
    public int EffectRuns => Volatile.Read(ref _effectRuns);

    /// <summary>将效果的运行次数加一。</summary>
    public void CountEffectRun() => Interlocked.Increment(ref _effectRuns);

    /// <summary>创建激活实例，并从已提交日志恢复其状态及待处理工作流。</summary>
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

/// <summary>由 <see cref="DurableTaskReplayHarness"/> 承载的 Grain 的一个激活实例。</summary>
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

    /// <summary>获取每次日志写入收集到的 Grain 计数器、其日志的条目数及已完成的任务。</summary>
    public IReadOnlyCollection<(int Counter, int Entries, TaskId[] Completed)> Writes => probe.Writes;

    /// <summary>在 <see cref="FailWrites"/> 之后有写入失败时完成。</summary>
    public Task FailedWrite => probe.FailedWrite;

    /// <summary>
    /// 使后续每次日志写入都在到达存储前失败，因此激活实例从现在起暂存的变更
    /// 会在激活实例结束时丢失，与 Silo 在写入前崩溃的情况相同。
    /// </summary>
    public void FailWrites() => probe.FailWrites();

    /// <summary>采用与客户端相同的方式调度测试工具的工作流。</summary>
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

    /// <summary>结束激活实例，不再执行日志写入。</summary>
    public async ValueTask DisposeAsync()
    {
        await Runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await scope.DisposeAsync();
    }
}

/// <summary>一个激活实例的 Grain 实例：其日志化计数器、日志及工作流的控制机制。</summary>
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

    /// <summary>获取 Grain 的日志化状态。</summary>
    public IDurableValue<int> Counter { get; } = counter;

    /// <summary>获取 Grain 的持久化日志，工作流向其中发布进度。</summary>
    public IDurableLog<int> Log { get; } = log;

    /// <summary>工作流在门控处等待时完成。</summary>
    public Task AtGate => _atGate.Task;

    /// <summary>等待测试打开门控，或等待执行尝试被中止。</summary>
    public Task WaitAtGateAsync()
    {
        _atGate.TrySetResult();
        return _gate.Task.WaitAsync(DurableTask.Current.CancellationToken);
    }

    public void OpenGate() => _gate.TrySetResult();

    /// <summary>请求一次日志写入，提交该写入运行时所有已暂存的内容，与其他 Grain 工作所做的操作相同。</summary>
    public void RequestUnrelatedWrite() => _unrelatedWrites.Enqueue(manager.WriteStateAsync().AsTask());

    /// <summary>等待工作流请求的不相关写入。</summary>
    public Task UnrelatedWrites => Task.WhenAll(_unrelatedWrites);

    public DurableTask CreateWorkflow() => Harness.Workflow(this);
}

/// <summary>不实际写入、记录每次写入所收集的内容且能够使写入失败的日志状态机。</summary>
internal sealed class WriteProbe(Func<(int Counter, int Entries, TaskId[] Completed)> snapshot) : IStateMachine
{
    private readonly ConcurrentQueue<(int Counter, int Entries, TaskId[] Completed)> _writes = new();
    private readonly TaskCompletionSource _failedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _failsWrites;

    public IReadOnlyCollection<(int Counter, int Entries, TaskId[] Completed)> Writes => _writes;

    /// <summary>探针使一次写入失败时完成。</summary>
    public Task FailedWrite => _failedWrite.Task;

    /// <summary>使后续每次写入的准备过程失败，从而使后续写入均无法到达存储。</summary>
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

/// <summary>可序列化的请求，用于运行其目标激活实例的工作流。</summary>
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
