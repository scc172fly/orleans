using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Jobs;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DurableTaskRecoveryCollection
{
    public const string Name = "Durable task activation recovery";
}

/// <summary>运行激活恢复覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskActivationRecoveryTests() : DurableTaskActivationRecoveryTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行激活恢复覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskActivationRecoveryTests() : DurableTaskActivationRecoveryTests(DurableTaskRecoveryFixture.OrleansBinary);

public abstract class DurableTaskActivationRecoveryTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskRecoveryFixture _fixture = new(journalFormatKey);

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData("default-durable-rpc-round-trip")]
    [InlineData("escaped/root\\identity")]
    public async Task ExistingTerminalTaskRecoversAcrossActivationWithoutReexecution(string rootId)
    {
        const int argument = 41;
        const int expectedResult = 130;
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        var activationBefore = await grain.GetActivationIdAsync();

        var scheduled = await grain.ComputeAsync(argument).WithId(rootId).ScheduleAsync(cancellationToken);
        Assert.Equal(expectedResult, await scheduled.WaitAsync(cancellationToken));
        Assert.Equal(
            new DurableTaskInvocationSnapshot(1, activationBefore, argument),
            _fixture.Probe.GetInvocation(grainId));
        var attachedBefore = grain.GetDurableTask<int>(rootId);
        Assert.Equal(expectedResult, await attachedBefore.WaitAsync(cancellationToken));

        await grain.RequestDeactivationAsync();
        var activationAfter = await grain.GetActivationIdAsync();
        await _fixture.Probe.WaitForActivationCountAsync(grainId, 2, cancellationToken);
        var attached = grain.GetDurableTask<int>(rootId);
        var resultAfter = await attached.WaitAsync(cancellationToken);

        Assert.Equal(TaskId.CreateRoot(rootId), attached.Id);
        Assert.Equal(expectedResult, resultAfter);
        Assert.NotEqual(activationBefore, activationAfter);
        Assert.Equal(
            new DurableTaskInvocationSnapshot(1, activationBefore, argument),
            _fixture.Probe.GetInvocation(grainId));
    }

    [Fact]
    public async Task PendingTaskReplaysPersistedLogicalTimeAcrossActivation()
    {
        const string rootId = "logical-time-replay";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        var scheduled = await grain.ObserveLogicalTimeAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 1, cancellationToken);

        await grain.RequestDeactivationAsync();
        _ = await grain.GetActivationIdAsync();
        var attached = grain.GetDurableTask<DateTimeOffset>(rootId);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 2, cancellationToken);
        var observations = _fixture.Probe.GetLogicalTimes(grainId);

        Assert.Equal(2, observations.Count);
        Assert.Equal(observations[0], observations[1]);
        Assert.False(await attached.IsCompletedAsync(
            new PollingOptions { PollTimeout = TimeSpan.Zero },
            cancellationToken));
        await attached.CancelAsync(cancellationToken);
    }

    [Fact]
    public async Task DelayedTaskResumesWhenTheDurableJobsClockPassesItsDueTime()
    {
        const string rootId = "delay-resume";
        const int argument = 5;
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();

        // 只有“持久工作”才有进步。任务的合理时间,也就是延迟的到期时间,都跟着这个时钟。
        _fixture.JobClock.Advance(TimeSpan.FromHours(2));
        var before = _fixture.JobClock.GetUtcNow();
        var scheduled = await grain.DelayThenComputeAsync(argument).WithId(rootId).ScheduleAsync(cancellationToken);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 1, cancellationToken);
        Assert.InRange(Assert.Single(_fixture.Probe.GetLogicalTimes(grainId)), before, _fixture.JobClock.GetUtcNow());
        Assert.False(await scheduled.IsCompletedAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, cancellationToken));

        _fixture.JobClock.Advance(TimeSpan.FromHours(2));
        var response = await scheduled.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal((argument * 3) + 7, response.GetResult<int>());
        Assert.Single(_fixture.Probe.GetLogicalTimes(grainId));
        Assert.Equal((argument * 3) + 7, await grain.GetDurableTask<int>(rootId).WaitAsync(cancellationToken));
    }

    [Fact]
    public async Task DelayedTaskSurvivesDeactivationAndCompletesWhenDueAfterReactivation()
    {
        const string rootId = "delay-across-deactivation";
        const int argument = 9;
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        var scheduled = await grain.DelayAcrossDeactivationAsync(argument).WithId(rootId).ScheduleAsync(cancellationToken);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 1, cancellationToken);

        // 延迟时间为一小时,所以在整个失能和随后的重播之间都有待于完成。
        await DeactivateAsync(grain, cancellationToken);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 2, cancellationToken);
        var attached = grain.GetDurableTask<int>(rootId);
        Assert.False(await attached.IsCompletedAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, cancellationToken));

        // 一旦“持久工作”时间过了适当时间,下一次启动程序将恢复计划。
        // 只有在激活保持延迟状态完全停止后, 启动才提前推进, 这样激活无法处理
        // 复选,而下一次激活则重播任务。
        Assert.True(_fixture.Cluster.TryGetGrainContext(grainId, out var activation));
        var activations = _fixture.Probe.GetActivationCount(grainId);
        await grain.RequestDeactivationAsync();
        await activation.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        _fixture.JobClock.Advance(TimeSpan.FromHours(2));
        _ = await grain.GetActivationIdAsync();
        await _fixture.Probe.WaitForActivationCountAsync(grainId, activations + 1, cancellationToken);
        var response = await attached.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(DurableTaskStatus.CompletedSuccessfully, response.Status);
        Assert.Equal((argument * 3) + 7, response.GetResult<int>());
        Assert.Equal((argument * 3) + 7, await scheduled.WaitAsync(cancellationToken));
        var observations = _fixture.Probe.GetLogicalTimes(grainId);
        Assert.True(observations.Count >= 3);
        Assert.All(observations, observation => Assert.Equal(observations[0], observation));
    }

    [Fact]
    public async Task RecoveredExecutionStartsAfterTheGrainActivationCompletes()
    {
        const string rootId = "activation-order";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        _ = await grain.ObserveActivationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await _fixture.Probe.WaitForActivationObservationCountAsync(grainId, 1, cancellationToken);

        // 下个启动 OnActivateAsync 返回到激活前排队的工作, 所以恢复
        // 启动时开始的操作在激活寿命周期内将在其启动前观察 grain 。
        await DeactivateAsync(grain, cancellationToken);
        await _fixture.Probe.WaitForActivationObservationCountAsync(grainId, 2, cancellationToken);

        Assert.Equal([true, true], _fixture.Probe.GetActivationObservations(grainId));
        await grain.GetDurableTask<bool>(rootId).CancelAsync(cancellationToken);
    }

    [Fact]
    public async Task DeactivationSignalsRunningExecutionsToAbortBeforeTheGrainDeactivates()
    {
        const string rootId = "deactivation-order";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Client.GetGrain<IDurableTaskRecoveryTestGrain>(Guid.NewGuid());
        var grainId = grain.GetGrainId();
        _ = await grain.ObserveDeactivationAsync().WithId(rootId).ScheduleAsync(cancellationToken);
        await _fixture.Probe.WaitForActivationObservationCountAsync(grainId, 1, cancellationToken);

        // grain的 OnDeactivateAsync 等待执行中止,否则运行时间将中止
        // 只在 OnDeactivateAsync 返回后发出信号。
        await DeactivateAsync(grain, cancellationToken);

        Assert.Equal([true], _fixture.Probe.GetDeactivationObservations(grainId));
        await grain.GetDurableTask<bool>(rootId).CancelAsync(cancellationToken);
    }

    private async Task DeactivateAsync(IDurableTaskRecoveryTestGrain grain, CancellationToken cancellationToken)
    {
        var activations = _fixture.Probe.GetActivationCount(grain.GetGrainId());
        await grain.RequestDeactivationAsync();
        _ = await grain.GetActivationIdAsync();
        await _fixture.Probe.WaitForActivationCountAsync(grain.GetGrainId(), activations + 1, cancellationToken);
    }
}

public interface IDurableTaskRecoveryTestGrain : IGrainWithGuidKey
{
    DurableTask<int> ComputeAsync(int value);
    DurableTask<DateTimeOffset> ObserveLogicalTimeAsync();
    DurableTask<int> DelayThenComputeAsync(int value);
    DurableTask<int> DelayAcrossDeactivationAsync(int value);
    DurableTask<bool> ObserveActivationAsync();
    DurableTask<bool> ObserveDeactivationAsync();
    Task<Guid> GetActivationIdAsync();
    Task RequestDeactivationAsync();
}

public readonly record struct DurableTaskInvocationSnapshot(int Count, Guid ActivationId, int Argument);

public sealed class DurableTaskRecoveryProbe
{
    private readonly ConcurrentDictionary<GrainId, DurableTaskInvocationSnapshot> _invocations = [];
    private readonly ConcurrentDictionary<GrainId, int> _activations = [];
    private readonly ConcurrentDictionary<GrainId, List<DateTimeOffset>> _logicalTimes = [];
    private readonly ConcurrentDictionary<GrainId, List<bool>> _activationObservations = [];
    private readonly ConcurrentDictionary<GrainId, List<bool>> _deactivationObservations = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>记录运行中的耐执行是否在 grain停止使用期间被信号中止。</summary>
    public void RecordDeactivationObservation(GrainId grainId, bool aborted)
    {
        var values = _deactivationObservations.GetOrAdd(grainId, static _ => []);
        lock (values)
        {
            values.Add(aborted);
        }

        SignalChanged();
    }

    public IReadOnlyList<bool> GetDeactivationObservations(GrainId grainId)
    {
        if (!_deactivationObservations.TryGetValue(grainId, out var values))
        {
            return [];
        }

        lock (values)
        {
            return [.. values];
        }
    }

    /// <summary>记录耐久执行是否观察到完全激活 grain.</summary>
    public void RecordActivationObservation(GrainId grainId, bool activated)
    {
        var values = _activationObservations.GetOrAdd(grainId, static _ => []);
        lock (values)
        {
            values.Add(activated);
        }

        SignalChanged();
    }

    public IReadOnlyList<bool> GetActivationObservations(GrainId grainId)
    {
        if (!_activationObservations.TryGetValue(grainId, out var values))
        {
            return [];
        }

        lock (values)
        {
            return [.. values];
        }
    }

    public async Task WaitForActivationObservationCountAsync(
        GrainId grainId,
        int expected,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (GetActivationObservations(grainId).Count < expected)
        {
            Task changed;
            lock (_activations)
            {
                if (GetActivationObservations(grainId).Count >= expected)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }

    public void RecordActivation(GrainId grainId)
    {
        _activations.AddOrUpdate(grainId, 1, static (_, count) => count + 1);
        SignalChanged();
    }

    public void RecordInvocation(GrainId grainId, Guid activationId, int argument)
    {
        _invocations.AddOrUpdate(
            grainId,
            _ => new DurableTaskInvocationSnapshot(1, activationId, argument),
            (_, current) => new DurableTaskInvocationSnapshot(current.Count + 1, activationId, argument));
        SignalChanged();
    }

    public int GetActivationCount(GrainId grainId) => _activations.TryGetValue(grainId, out var count) ? count : 0;

    public DurableTaskInvocationSnapshot GetInvocation(GrainId grainId) =>
        _invocations.TryGetValue(grainId, out var snapshot) ? snapshot : default;

    public void RecordLogicalTime(GrainId grainId, DateTimeOffset value)
    {
        var values = _logicalTimes.GetOrAdd(grainId, static _ => []);
        lock (values)
        {
            values.Add(value);
        }

        SignalChanged();
    }

    public IReadOnlyList<DateTimeOffset> GetLogicalTimes(GrainId grainId)
    {
        if (!_logicalTimes.TryGetValue(grainId, out var values))
        {
            return [];
        }

        lock (values)
        {
            return [.. values];
        }
    }

    public async Task WaitForActivationCountAsync(
        GrainId grainId,
        int expected,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!_activations.TryGetValue(grainId, out var count) || count < expected)
        {
            Task changed;
            lock (_activations)
            {
                if (_activations.TryGetValue(grainId, out count) && count >= expected)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }

    public async Task WaitForLogicalTimeCountAsync(
        GrainId grainId,
        int expected,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (GetLogicalTimes(grainId).Count < expected)
        {
            Task changed;
            lock (_activations)
            {
                if (GetLogicalTimes(grainId).Count >= expected)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }

    private void SignalChanged()
    {
        lock (_activations)
        {
            _changed.TrySetResult();
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

[GrainType("durable-task-recovery-test")]
public sealed class DurableTaskRecoveryTestGrain(DurableTaskRecoveryProbe probe)
    : DurableGrain, IDurableTaskRecoveryTestGrain
{
    private readonly Guid _activationId = Guid.NewGuid();
    private bool _activated;
    private CancellationToken? _executionToken;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        probe.RecordActivation(this.GetGrainId());

        // 启动前的激活队列工作, 如回收程序, 在初始化完成之前运行 。
        await Task.Yield();
        _activated = true;
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_executionToken is { } executionToken)
        {
            // 如果执行在方法运行时没有信号, 停止操作的标记将结束等待 。
            var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (executionToken.Register(() => aborted.TrySetResult()))
            using (cancellationToken.Register(() => aborted.TrySetResult()))
            {
                await aborted.Task;
            }

            probe.RecordDeactivationObservation(this.GetGrainId(), executionToken.IsCancellationRequested);
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async DurableTask<bool> ObserveActivationAsync()
    {
        probe.RecordActivationObservation(this.GetGrainId(), _activated);
        await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("wait");
        return _activated;
    }

    public async DurableTask<bool> ObserveDeactivationAsync()
    {
        _executionToken = DurableTask.Current.CancellationToken;
        probe.RecordActivationObservation(this.GetGrainId(), _activated);
        await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("wait");
        return _activated;
    }

    public DurableTask<int> ComputeAsync(int value)
    {
        probe.RecordInvocation(this.GetGrainId(), _activationId, value);
        return DurableTask.FromResult(checked((value * 3) + 7));
    }

    public async DurableTask<DateTimeOffset> ObserveLogicalTimeAsync()
    {
        var value = await ObserveCommittedLogicalTimeAsync();
        await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("wait");
        return value;
    }

    public async DurableTask<int> DelayThenComputeAsync(int value)
    {
        probe.RecordLogicalTime(this.GetGrainId(), DurableTask.Current.UtcNow);
        await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("pause");
        return checked((value * 3) + 7);
    }

    public async DurableTask<int> DelayAcrossDeactivationAsync(int value)
    {
        _ = await ObserveCommittedLogicalTimeAsync();
        await DurableTask.Delay(TimeSpan.FromHours(1)).WithId("pause");
        return checked((value * 3) + 7);
    }

    // 执行过程的逻辑时间是用第一个耐久写作来承诺。一旦完成一个步骤,它就会在写作时完成,所以
    // 探测器只观察重播读回的时间。
    private async Task<DateTimeOffset> ObserveCommittedLogicalTimeAsync()
    {
        var value = DurableTask.Current.UtcNow;
        await DurableTask.Run(static _ => { }).WithId("commit");
        probe.RecordLogicalTime(this.GetGrainId(), value);
        return value;
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 主持一个进程内群组,该组的 grains 日记任务状态、信息传递和耐久工作以一个日记格式。
/// </summary>
internal sealed class DurableTaskRecoveryFixture : IAsyncLifetime
{
    public const string Json = "json";
    public const string OrleansBinary = "orleans-binary";

    public DurableTaskRecoveryFixture(string journalFormatKey, Action<ISiloBuilder>? configureSilo = null)
    {
        Probe = new();
        MessagingProbe = new();
        JobClock = new();
        Storage = new VolatileJournalStorageProvider(
            Options.Create(new JournaledStateManagerOptions { JournalFormatKey = journalFormatKey }));
        var clusterId = $"durable-task-recovery-{Guid.NewGuid():N}";
        var serviceId = $"durable-task-recovery-service-{Guid.NewGuid():N}";
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureClient(clientBuilder =>
        {
            clientBuilder.AddDurableTasks();
            clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });
        });
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });
            siloBuilder.AddJournaling();
            siloBuilder.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
            siloBuilder.UseInMemoryDurableJobs();
            siloBuilder.Services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, JobClock);
            // 执行运行时发生故障或停用,当恢复试验观察到时,会迅速恢复。
            siloBuilder.AddDurableTasks(options => options.RunnableExecutionWakeupDelay = TimeSpan.FromSeconds(1));
            siloBuilder.Services.AddSingleton(Probe);
            siloBuilder.Services.AddSingleton(MessagingProbe);
            siloBuilder.Services.RemoveAll<IJournalStorageProvider>();
            siloBuilder.Services.RemoveAll<IJournalStorageCatalog>();
            siloBuilder.Services.AddSingleton(Storage);
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(
                services => services.GetRequiredService<VolatileJournalStorageProvider>());
            siloBuilder.Services.AddSingleton<IJournalStorageCatalog>(
                services => services.GetRequiredService<VolatileJournalStorageProvider>());
            configureSilo?.Invoke(siloBuilder);
        });
        Cluster = builder.Build();
    }

    public InProcessTestCluster Cluster { get; }
    public IClusterClient Client => Cluster.Client!;
    public DurableTaskRecoveryProbe Probe { get; }
    public DurableTaskMessagingProbe MessagingProbe { get; }

    /// <summary>获得“持久工作时钟”,该时钟决定何时应发送诸如延迟恢复等排定信件。</summary>
    public AdvanceableTimeProvider JobClock { get; }
    public VolatileJournalStorageProvider Storage { get; }

    public ValueTask InitializeAsync() => new(Cluster.DeployAsync());
    public ValueTask DisposeAsync() => Cluster.DisposeAsync();
}

/// <summary>
/// 时钟随实时后加一个补分 测试可以推进
/// </summary>
/// <remarks>
/// 时间和时间戳随实时进行,所以“持久工作”持续运行;将适当时段推向过去,并
/// 长期的工作等待着一个碎片的下一个工作,
/// 工作已经等待时间过了
/// </remarks>
public sealed class AdvanceableTimeProvider : TimeProvider
{
    private readonly HashSet<AdvanceableTimer> _timers = [];
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new AdvanceableTimer(this, callback, state);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        Interlocked.Add(ref _offsetTicks, delta.Ticks);
        AdvanceableTimer[] timers;
        lock (_timers)
        {
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.Rearm();
        }
    }

    private void Remove(AdvanceableTimer timer)
    {
        lock (_timers)
        {
            _timers.Remove(timer);
        }
    }

    /// <summary>定时器的到期时间是其供应商钟表上的时段,预付款可以通过。</summary>
    /// <remarks>
    /// 真正的一发计时器等待剩余时间直到适当时间,并随时在钟头推到前重新武装
    /// 或定期定时火, 时间未到的射击, 时间尚未到, 重新武装可以留下,等待
    /// 而不是再来一次。
    /// </remarks>
    private sealed class AdvanceableTimer : ITimer
    {
        private readonly AdvanceableTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly ITimer _timer;
        private readonly object _lock = new();
        private DateTimeOffset? _dueAt;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public AdvanceableTimer(AdvanceableTimeProvider provider, TimerCallback callback, object? state)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            _timer = TimeProvider.System.CreateTimer(static timer => ((AdvanceableTimer)timer!).Fire(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _provider.GetUtcNow() + dueTime;
                return ArmLocked();
            }
        }

        public void Rearm()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    ArmLocked();
                }
            }
        }

        private void Fire()
        {
            lock (_lock)
            {
                if (_disposed || _dueAt is not { } dueAt)
                {
                    return;
                }

                if (_provider.GetUtcNow() < dueAt)
                {
                    ArmLocked();
                    return;
                }

                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : _provider.GetUtcNow() + _period;
                ArmLocked();
            }

            _callback(_state);
        }

        private bool ArmLocked()
        {
            var remaining = _dueAt is { } dueAt ? dueAt - _provider.GetUtcNow() : Timeout.InfiniteTimeSpan;
            return _timer.Change(remaining < TimeSpan.Zero && _dueAt is not null ? TimeSpan.Zero : remaining, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _dueAt = null;
            }

            _timer.Dispose();
            _provider.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
