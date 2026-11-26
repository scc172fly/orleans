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
/// 主持一个在编集的集群,其日记存储、发箱交付、收信箱参与者和短信提醒唤醒活动
/// 通过 a <see cref="DurableTaskFaultInjector"/>,通过注入缺陷驱动工作流程完成。
/// </summary>
/// <remarks>
/// <para>
/// 持久就业时钟是领导 grains时钟时间 <see cref="JobClockLead"/>以持续的工作再试,
/// 排 grains时间, 时间一有安排就到期。 工作流程的延迟和超时最后
/// <see cref="WorkflowDelay"/>,所以一旦实时通过它们就到期。
/// <see cref="RetryInterval"/>绳子在等待时不能提前
/// </para>
/// <para>
/// 利用功能只能同步地进行事件: 通过客户等待其回应完成任务,
/// 通过 grains信息提醒的持久就业活动而缺乏能力;见 <see cref="SettleAsync"/>。
/// <see cref="RunTimeout"/> 将每个等待的值限制为检测一个摊位。
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

    // 上次发生故障的 silo 崩溃, 以及下次崩溃开始时取消的标记 。
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
    /// 获取上次发生故障的 silo 崩溃, 一旦集群宣布它已死亡, 就会完成该崩溃 。
    /// </summary>
    /// <remarks>
    /// 崩溃开始时客户端的通话在飞行中, 因为坠机 silo 可能永远无法回答: a
    /// 客户端失败了一个调用, 只有在调用响应超时过后, 客户端才会回答不 silo 的调用。
    /// 长以 <see cref="RunTimeout"/>。当坠机事故结束后,会重新查寻那些同时失败的电话和通话。
    /// 崩溃时的组合 silos 设置可持久的工作 <c>ShardCheckInterval</c> 短,所以剩下的 silo 通过
    /// 崩溃 silo的提醒迅速恢复 。
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

    /// <summary>安排根任务,等待其终端响应。</summary>
    /// <remarks>
    /// 当基础设施故障使其失效时,例如当 grain's
    /// 激活无法读取日记、 写入失败, 或者客户端等待时停止使用。 任务编号
    /// 使时间表变得复杂,结果只是任务的反应。
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

    /// <summary>等到 <paramref name="grains"/> 信息工作无效 。</summary>
    /// <remarks>
    /// A grain 保存信息工作,而其收件箱、发件箱或持久任务运行时间的工作源报告
    /// 无法执行的处决,确实。 grain的日记写前的电讯提醒
    /// 等待完成的工作可以持久,只有在没有剩余工作时,提醒工作才能完成。 grain 因此,保留
    /// 预定的每个提醒工作一旦完成,就立即进行。
    /// grains最后的工作,例如他们的信息所产生的结果。
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

    /// <summary>呼叫 grain ,直到呼唤成功,同时重新尝试基础设施故障。</summary>
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
            // 尝试开始于 silo 撞车事故等待到集群组宣布它死亡,所以它到达
            // grain的下一次激活; 当崩溃开始时正在飞行中的, 因为坠机 silo 可能永远不会
            // 客户在响应超时后才会拒绝接听。
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
    /// 通过 " 持久工作 " 活动,跟踪每个 grain都拥有的 " 持久工作 " 信息提醒工作。
    /// </summary>
    /// <remarks>
    /// 一项工作从安排活动结束直至完成该工作结束,一直未完成。
    /// 删除: 已完成、 没有重试失败或取消。 重新排定和重新审阅运行会保持未完成 。
    /// 即时到期的工作可在预定的活动结束之前运行和结束,所以结束的工作
    /// 永不记念,
    /// </remarks>
    private sealed class MessagingWakeupTracker : IDisposable
    {
        // 追踪者所读到的“持久工作”活动和标签的名称。
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

                // 摊位的诊断书描述了每个杰出的工作活动
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
