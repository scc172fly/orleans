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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行激活恢复相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskActivationRecoveryTests() : DurableTaskActivationRecoveryTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行激活恢复相关测试。</summary>
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

        // 仅持久化作业时钟向前推进。任务的逻辑时间及其延迟到期时间均遵循该时钟。
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

        // 延迟时长为一小时，因此在停用及随后的重放过程中仍保持待完成状态。
        await DeactivateAsync(grain, cancellationToken);
        await _fixture.Probe.WaitForLogicalTimeCountAsync(grainId, 2, cancellationToken);
        var attached = grain.GetDurableTask<int>(rootId);
        Assert.False(await attached.IsCompletedAsync(new PollingOptions { PollTimeout = TimeSpan.Zero }, cancellationToken));

        // 持久化作业时钟越过到期时间后，下一次激活处理计划的恢复操作。该时钟
        // 仅在持有延迟的激活完全停用后才向前推进，因此该激活无法处理
        // 恢复操作，下一次激活将重放任务。
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

        // 下一次激活的 OnActivateAsync 将执行权让给该激活此前排队的工作，因此若恢复的
        // 执行在激活生命周期期间启动，就会观察到尚未初始化的 Grain。
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

        // Grain 的 OnDeactivateAsync 等待正在运行的执行收到中止信号，否则运行时将
        // 仅在 OnDeactivateAsync 返回后发送该信号。
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

    /// <summary>记录在 Grain 停用期间，是否向正在运行的持久化执行发出了中止信号。</summary>
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

    /// <summary>记录持久化执行是否观察到了已完全激活的 Grain。</summary>
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

        // 激活在此方法之前排队的工作（例如恢复工作）会在初始化完成前运行。
        await Task.Yield();
        _activated = true;
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_executionToken is { } executionToken)
        {
            // 如果此方法运行期间未向执行发送信号，则停用令牌结束等待。
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

    // 执行实例的逻辑时间随其首次持久化写入提交。步骤在其写入提交后完成，因此
    // 探针仅观察重放时能够读回的时间。
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
/// 托管进程内集群，其 Grain 使用同一种日志格式记录任务状态、消息传递和持久化作业。
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
            // 执行运行期间发生崩溃或停用时，会迅速恢复执行，供恢复测试观察。
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

    /// <summary>获取持久化作业时钟，该时钟决定延迟恢复等计划消息何时到期。</summary>
    public AdvanceableTimeProvider JobClock { get; }
    public VolatileJournalStorageProvider Storage { get; }

    public ValueTask InitializeAsync() => new(Cluster.DeployAsync());
    public ValueTask DisposeAsync() => Cluster.DisposeAsync();
}

/// <summary>
/// 遵循真实时间加上可由测试推进的偏移量的时钟。
/// </summary>
/// <remarks>
/// 计时器和时间戳遵循真实时间，因此持久化作业持续运行；推进时钟会使到期时间落入过去，且
/// 到期时间被越过的计时器会立即触发。持久化作业使用此类计时器等待分片的下一个到期作业，
/// 因此已在等待的作业会在时钟越过其到期时间后运行。
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

    /// <summary>到期时间以其提供程序的时钟为准、可通过推进时钟越过的计时器。</summary>
    /// <remarks>
    /// 实际的一次性计时器等待距到期时间的剩余时长，每次时钟推进
    /// 或周期性计时器触发时都会重新设置。如果重新设置后仍发生一次尚未到期的触发，则
    /// 重新等待。
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
