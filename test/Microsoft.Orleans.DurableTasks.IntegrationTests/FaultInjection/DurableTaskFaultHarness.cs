using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Diagnostics;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 托管进程内集群，其日志存储、发件箱投递、收件箱参与者及消息传递唤醒均经过
/// <see cref="DurableTaskFaultInjector"/>，并在注入故障的情况下驱动工作流完成。
/// </summary>
/// <remarks>
/// <para>
/// Durable Jobs 的时钟比 Grain 的时钟提前 <see cref="JobClockLead"/>，因此遵循
/// Grain 时钟的 Durable Jobs 重试一经调度即到期。工作流的延迟和超时持续
/// <see cref="WorkflowDelay"/>，因此实际时间经过这段时长即到期。发件箱重试遵循
/// <see cref="RetryInterval"/>。测试工具在等待期间不推进时钟。
/// </para>
/// <para>
/// 测试工具仅通过事件同步：通过客户端等待任务响应来同步任务完成，并通过
/// Grain 消息传递唤醒所对应的 Durable Jobs 活动来同步静止状态；参见 <see cref="SettleAsync"/>。
/// <see cref="RunTimeout"/> 限定每次等待的时长，以检测停滞。
/// </para>
/// </remarks>
internal sealed class DurableTaskFaultHarness : IAsyncLifetime
{
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan JobClockLead = TimeSpan.FromHours(1);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan WorkflowDelay = TimeSpan.FromMilliseconds(10);
    private const int MaxClientAttempts = 20;
    private readonly MessagingWakeupTracker _wakeups = new();

    // 记录上一次由故障触发的 Silo 崩溃，以及一个在下一次崩溃开始时被取消的令牌。
    private readonly object _crashLock = new();
    private Task _crash = Task.CompletedTask;
    private CancellationTokenSource _crashing = new();

    public DurableTaskFaultHarness(string journalFormatKey, Action<ISiloBuilder>? configureSilo = null)
    {
        Injector = new() { CrashSilo = OnCrashSilo };
        Gates = new();
        Fixture = new(journalFormatKey, silo =>
        {
            Injector.Configure(silo);
            silo.Services.AddSingleton(Gates);
            silo.Services.Configure<DurableMessagingOptions>(options => options.RetryInitialInterval = RetryInterval);

            configureSilo?.Invoke(silo);
        });
    }

    public DurableTaskFaultInjector Injector { get; }
    public FaultTestGates Gates { get; }
    public DurableTaskRecoveryFixture Fixture { get; }
    public IClusterClient Client => Fixture.Client;

    /// <summary>
    /// 获取上一次由故障触发的 Silo 崩溃操作；当集群宣告该 Silo 已失效时，此操作完成。
    /// </summary>
    /// <remarks>
    /// 崩溃开始时客户端正在进行的调用会结束，因为崩溃的 Silo 可能永远不回复：
    /// 只有调用的响应超时到期时，客户端才会使没有任何 Silo 回复的调用失败；默认时长与
    /// <see cref="RunTimeout"/> 相同。这些调用以及期间失败的调用均在崩溃处理完成后重试。对于
    /// 会使 Silo 崩溃的集群，将 Durable Jobs 的 <c>ShardCheckInterval</c> 设为较短间隔，使剩余的 Silo 能及时接管
    /// 已崩溃 Silo 的唤醒操作。
    /// </remarks>
    public Task Crash
    {
        get
        {
            lock (_crashLock)
            {
                return _crash;
            }
        }
    }

    public async ValueTask InitializeAsync()
    {
        Fixture.JobClock.Advance(JobClockLead);
        await Fixture.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Injector.Reset();
        await Crash;
        await Fixture.DisposeAsync();
        _wakeups.Dispose();
    }

    /// <summary>调度根任务并等待其终态响应。</summary>
    /// <remarks>
    /// 当基础设施故障导致客户端调用失败时，会立即重试，例如 Grain
    /// 激活时读取日志失败、写入失败，或在客户端等待期间停用。任务 ID
    /// 用于调度去重，只有任务响应才是最终结果。
    /// </remarks>
    public async Task<DurableTaskResponse> RunAsync<T>(
        IAddressable grain,
        string rootId,
        Func<CancellationToken, Task> schedule,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        try
        {
            await RetryAsync(schedule, failures, cancellationToken).WaitAsync(RunTimeout, cancellationToken);
            var reference = grain.GetDurableTask<T>(rootId);
            DurableTaskResponse response = null!;
            await RetryAsync(async token => response = await reference.GetResponseAsync(token), failures, cancellationToken)
                .WaitAsync(RunTimeout, cancellationToken);
            return response;
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out after {RunTimeout} waiting for task '{rootId}' on {grain.GetGrainId()}. Client call failures "
                + $"({failures.Count}): {string.Join(" | ", failures.Distinct().Take(5))}",
                exception);
        }
    }

    /// <summary>等待 <paramref name="grains"/> 中每个 Grain 都不再持有消息传递工作。</summary>
    /// <remarks>
    /// 当 Grain 的收件箱、发件箱或持久化任务运行时的工作源（后者报告
    /// 可运行的执行实例）持有工作时，Grain 就持有消息传递工作。收件箱在使
    /// 工作进入待处理状态的日志写入变为持久状态之前，调度 Grain 的消息传递唤醒；唤醒作业仅在没有任何工作剩余时完成。因此，当为 Grain 调度的
    /// 每个唤醒作业均已完成时，Grain 不再持有工作。应在某个发生于 Grain
    /// 最后一项工作之后的结果（例如由其消息产生的结果）出现后调用此方法。
    /// </remarks>
    public async Task SettleAsync(IEnumerable<GrainId> grains, CancellationToken cancellationToken)
    {
        var targets = grains.ToList();
        try
        {
            await _wakeups.WaitForIdleAsync(targets).WaitAsync(RunTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out after {RunTimeout} waiting for the grains to hold no messaging work, at "
                + $"{Fixture.JobClock.GetUtcNow():O} on the Durable Jobs clock. Outstanding wake-up jobs: "
                + _wakeups.DescribeOutstanding(targets),
                exception);
        }
    }

    /// <summary>调用 Grain 直至调用成功，发生基础设施故障时立即重试。</summary>
    public async Task<TResult> CallAsync<TResult>(Func<CancellationToken, Task<TResult>> call, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        TResult result = default!;
        await RetryAsync(async token => result = await call(token), failures, cancellationToken);
        return result;
    }

    private async Task RetryAsync(Func<CancellationToken, Task> call, List<string> failures, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Silo 崩溃期间开始的尝试会等待集群宣告其失效，然后到达
            // Grain 的下一次激活；崩溃开始时正在进行的尝试会结束，因为崩溃的 Silo 可能永远不
            // 回复，且客户端只有在响应超时到期时才会使这类调用失败。
            Task crash;
            CancellationToken crashing;
            lock (_crashLock)
            {
                crash = _crash;
                crashing = _crashing.Token;
            }

            await crash;
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, crashing);
            try
            {
                await call(attemptCancellation.Token);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is not (DurableTaskConflictException or TimeoutException)
                && attempt < MaxClientAttempts)
            {
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    private void OnCrashSilo(SiloAddress address)
    {
        lock (_crashLock)
        {
            var crashing = _crashing;
            _crashing = new();
            _crash = Task.Run(async () =>
            {
                crashing.Cancel();
                var silo = Fixture.Cluster.Silos.Single(silo => silo.SiloAddress.Equals(address));
                await Fixture.Cluster.KillSiloAsync(silo, CancellationToken.None);
                await Fixture.Cluster.WaitForLivenessToStabilizeAsync(didKill: true);
            });
        }
    }

    /// <summary>
    /// 通过 Durable Jobs 活动跟踪其为每个 Grain 持有的消息传递唤醒作业。
    /// </summary>
    /// <remarks>
    /// 从调度作业的活动结束起，直至某次执行结束且作业被移除，作业始终处于未完成状态；
    /// 移除的原因包括完成、失败且不重试或取消。重新调度及重试的运行仍保持未完成状态。若作业
    /// 立即到期，它可能在调度活动结束前就已运行并结束，因此已结束的作业会被
    /// 记录，且永远不会被标记为未完成。
    /// </remarks>
    private sealed class MessagingWakeupTracker : IDisposable
    {
        // 跟踪器读取的 Durable Jobs 活动及标记名称。
        private const string ScheduleActivity = "schedule durable job";
        private const string ExecuteActivity = "execute durable job";
        private const string JobIdTag = "orleans.durable_job.id";
        private const string JobNameTag = "orleans.durable_job.name";
        private const string TargetTag = "orleans.durable_job.target_grain_id";
        private const string StatusTag = "orleans.durable_job.status";
        private const string DueTimeTag = "orleans.durable_job.due_time";
        private const string MessagingPumpJobName = "orleans.messaging.pump";

        private readonly ActivityListener _listener;
        private readonly Dictionary<string, HashSet<string>> _outstanding = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ended = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _transitions = new(StringComparer.Ordinal);
        private readonly List<(HashSet<string> Grains, TaskCompletionSource Idle)> _waiters = [];

        public MessagingWakeupTracker()
        {
            _listener = new()
            {
                ShouldListenTo = static source => source.Name == ActivitySources.DurableJobsActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = OnActivityStopped,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Task WaitForIdleAsync(IEnumerable<GrainId> grains)
        {
            var keys = grains.Select(static grain => grain.ToString()).ToHashSet(StringComparer.Ordinal);
            lock (_outstanding)
            {
                if (IsIdle(keys))
                {
                    return Task.CompletedTask;
                }

                var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((keys, idle));
                return idle.Task;
            }
        }

        private void OnActivityStopped(Activity activity)
        {
            if (activity.GetTagItem(JobNameTag) as string != MessagingPumpJobName
                || activity.GetTagItem(TargetTag) is not string target
                || activity.GetTagItem(JobIdTag) is not string jobId)
            {
                return;
            }

            lock (_outstanding)
            {
                if (!_transitions.TryGetValue(jobId, out var transitions))
                {
                    _transitions[jobId] = transitions = [];
                }

                // 停滞诊断会描述每个未完成作业的活动。
                transitions.Add(
                    $"{activity.OperationName} due {activity.GetTagItem(DueTimeTag)} ended {activity.StartTimeUtc + activity.Duration:O}: "
                    + $"{activity.GetTagItem(StatusTag) ?? activity.Status.ToString()}");
                if (activity.OperationName == ScheduleActivity)
                {
                    if (!_ended.Contains(jobId))
                    {
                        if (!_outstanding.TryGetValue(target, out var jobs))
                        {
                            _outstanding[target] = jobs = new(StringComparer.Ordinal);
                        }

                        jobs.Add(jobId);
                    }
                }
                else if (activity.OperationName == ExecuteActivity
                    && activity.GetTagItem(StatusTag) is "completed" or "failed" or "cancellation_requested")
                {
                    _ended.Add(jobId);
                    if (_outstanding.TryGetValue(target, out var jobs) && jobs.Remove(jobId))
                    {
                        foreach (var waiter in _waiters.Where(waiter => IsIdle(waiter.Grains)).ToList())
                        {
                            _waiters.Remove(waiter);
                            waiter.Idle.TrySetResult();
                        }
                    }
                }
            }
        }

        public string DescribeOutstanding(IEnumerable<GrainId> grains)
        {
            lock (_outstanding)
            {
                return string.Join("; ", grains.Select(grain =>
                    $"{grain}: [{string.Join(", ", (_outstanding.TryGetValue(grain.ToString(), out var jobs) ? jobs : [])
                        .Select(job => $"{job} ({string.Join(" -> ", _transitions[job])})"))}]"));
            }
        }

        private bool IsIdle(HashSet<string> grains) =>
            grains.All(grain => !_outstanding.TryGetValue(grain, out var jobs) || jobs.Count == 0);

        public void Dispose() => _listener.Dispose();
    }
}
