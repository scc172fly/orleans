using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Durable.Jobs;
using Orleans.Concurrency;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 驱动箱处理和发件箱交付,以一次性激活。
/// </summary>
/// <remarks>
/// 激活调度器上有一个单一循环,只要有信号,就为两个队列提供服务。
/// 一项持久工作一直保持唤醒 grain ,而排队则持有启动程序能够完成的工作或回调
/// 已注册的 <see cref="IInbox.RegisterWakeup"/> 报告工作; 收件箱信息等待处理者, 不提醒 。
/// 工作阶段比目前觉醒时间提前,工作阶段较早,新工作、新工作、新工作、
/// 早期的觉醒在运行时完成 。
/// 觉醒时间可能很早、较晚或重复:每一通行证都从目前状态中得出工作成果。
/// 警醒是活环一收到信号即立即执行的工程的崩溃保险,因此工作
/// 已经到期需要 <see cref="RecheckDelay"/>内提醒,而书面工作只有在没有跟踪或记录时才安排时间。
/// 工作在运行中, 一次重新武装自己, 所以忙碌的 grain 就没有工作操作
/// 工作在激活失败时无限期地重试(见 <see cref="GetRetryTime"/>),并且
/// 激活从恢复状态重新武装它, 所以投入的工作总是保持警醒。
/// 启动调度器上的提醒簿:日记写它准备并和工作运行序列同步。
/// 循环让启动过程保持活力, 等待传票超编工作,
/// 只有在以后才应完成的工作依赖持久工作,这样激活就可以在等待期间收集。
/// </remarks>
internal sealed partial class MessagingPump : IDurableJobFeatureHandler, ILifecycleObserver
{
    internal const string JobName = "orleans.messaging.pump";
    internal static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(1);

    // 公共属性暴露了运行时间的内部密封策略类型。
    private static readonly Type StatelessWorkerPlacementType = new StatelessWorkerAttribute().PlacementStrategy.GetType();
    private readonly SingleWaiterAutoResetEvent _signal = new() { RunContinuationsAsynchronously = true };
    private readonly CancellationTokenSource _shutdown = new();
    private Inbox? _inbox;
    private Outbox? _outbox;
    private Task? _loop;
    private TaskCompletionSource? _nextPass;
    private Task<DurableJob>? _scheduling;
    private DateTimeOffset _schedulingDueTime;
    private bool _keepingAlive;

    // 下次提醒 grain 的工作, 如果有的话, 以及它应有的时间。 完成该工作的工作会清除它 。
    private string? _wakeupJobId;
    private DateTimeOffset _wakeupDueTime;

    // 工作完成时,只要工作完成,就可按前一个时代要求的时间表,该时间表可以属于有以下条件的工作:
    // 已经运行并完成, 因此它不再保证提醒。
    private int _wakeupEpoch;
    private int _schedulingEpoch;

    public MessagingPump(IGrainContext grainContext, IJournaledStateManager stateManager, IDurableJobHandlerRegistry jobHandlers, MessagingShared shared)
    {
        // 通信要求每个 grain 身份有一个激活,因此每个激活无国籍工人都失败。
        if (grainContext.GetComponent<PlacementStrategy>()?.GetType() == StatelessWorkerPlacementType)
        {
            throw new InvalidOperationException(
                $"Messaging requires one activation per grain identity, but grain type '{grainContext.GrainId.Type}' is a stateless worker.");
        }

        GrainContext = grainContext;
        StateManager = stateManager;
        Shared = shared;
        GrainTypeTag = shared.GetGrainTypeTag(grainContext.GrainId.Type);
        jobHandlers.Register(this);
        grainContext.ObservableLifecycle.Subscribe(nameof(MessagingPump), GrainLifecycleStage.Activate, this);

        // 等到 RecheckDelay 以后,应适当工作火力的唤醒时间,因此一旦激活完成并进行后循环即启动
        // 回收并重新进行分阶段工作,不等待一个。
        grainContext.ObservableLifecycle.Subscribe(nameof(MessagingPump), GrainLifecycleStage.Last, _ =>
        {
            EnsureStarted();
            return Task.CompletedTask;
        }, static _ => Task.CompletedTask);

        // 恢复深度报告在 SetupState期间,因此释放该深度时涵盖激活前失效的启动。
        grainContext.ObservableLifecycle.Subscribe(nameof(MessagingPump), GrainLifecycleStage.SetupState, static _ => Task.CompletedTask, _ =>
        {
            _inbox?.ReleaseDepth();
            _outbox?.ReleaseDepth();
            return Task.CompletedTask;
        });
    }

    public IGrainContext GrainContext { get; }

    public IJournaledStateManager StateManager { get; }

    public MessagingShared Shared { get; }

    /// <summary>获取此激活 grain 类型的公制标记。</summary>
    public string GrainTypeTag { get; }

    /// <summary>获取在信息停止启动时取消的标记。</summary>
    public CancellationToken ShutdownToken => _shutdown.Token;

    internal void Register(Inbox inbox) => _inbox = inbox;

    internal void Register(Outbox outbox) => _outbox = outbox;

    internal Inbox Inbox => _inbox ?? throw new InvalidOperationException("The messaging inbox has not been registered.");


    public bool CanHandle(string jobName) => string.Equals(jobName, JobName, StringComparison.Ordinal);

    /// <summary>申请办理通行证。</summary>
    public void Signal()
    {
        if (!_shutdown.IsCancellationRequested) _signal.Signal();
    }

    /// <summary>申请办理通行证,并确保为已可处理的工作持久地敲响警钟。</summary>
    public void RequestWakeup()
    {
        if (_shutdown.IsCancellationRequested) return;
        _signal.Signal();
        RepairWakeupAsync().Ignore();
    }

    /// <summary>启动服务启动后,激活服务 grain 电话一结束即启动循环。</summary>
    public void EnsureStarted()
    {
        if (_loop is null && !_shutdown.IsCancellationRequested)
        {
            _loop = RunAsync();
        }
    }

    /// <summary>
    /// 确保持久工作将唤醒这一风险 grain 不迟于 <paramref name="dueTime"/>,或
    /// <see cref="RecheckDelay"/> 工作时间早于 " 持久就业钟 " 。
    /// </summary>
    public async ValueTask EnsureWakeupAsync(DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        // 实时循环在工作到期后立即进行工作,因此任何跟踪或安排在 RecheckDelay内进行提醒
        // 引爆失败时盖子会盖住它
        var earliest = MessagingTime.AddClamped(Shared.JobTimeProvider.GetUtcNow(), RecheckDelay);
        if (dueTime < earliest) dueTime = earliest;
        if (_wakeupJobId is not null && _wakeupDueTime <= dueTime) return;
        if (_scheduling is null || _schedulingEpoch != _wakeupEpoch || _schedulingDueTime > dueTime)
        {
            _schedulingEpoch = _wakeupEpoch;
            _schedulingDueTime = dueTime;
            _scheduling = Shared.JobManager.ScheduleJobAsync(new ScheduleJobRequest
            {
                Target = GrainContext.GrainId,
                JobName = JobName,
                DueTime = _schedulingDueTime
            }, _shutdown.Token);
        }

        var scheduling = _scheduling;
        var epoch = _schedulingEpoch;
        try
        {
            var job = await scheduling.WaitAsync(cancellationToken).ConfigureAwait(true);
            if (epoch == _wakeupEpoch && (_wakeupJobId is null || job.DueTime < _wakeupDueTime))
            {
                _wakeupJobId = job.Id;
                _wakeupDueTime = job.DueTime;
            }
        }
        finally
        {
            if (scheduling.IsCompleted && ReferenceEquals(_scheduling, scheduling)) _scheduling = null;
        }
    }

    public async ValueTask<DurableJobRunResult> ExecuteJobAsync(IJobRunContext context, CancellationToken cancellationToken)
    {
        // 正在运行的工作不断重新安排时间,直到排队和每个登记醒醒的单位都报告没有工作。
        var jobId = context.Job.Id;
        if (_wakeupJobId is null)
        {
            _wakeupJobId = jobId;
            _wakeupDueTime = context.Job.DueTime;
        }
        EnsureStarted();
        var pass = _nextPass ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        _signal.Signal();
        try
        {
            await pass.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            // 失灵的通行证只能留下分阶段的清除;其工作必须能在激活失败后得以维持。
            _shutdown.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            // 持久工作重试失败的运行(见 GetRetryTime), 从而唤醒恢复的激活 。
            CompleteWakeup(jobId);
            throw;
        }

        var now = Shared.JobTimeProvider.GetUtcNow();
        if (GetNextWakeupDelay(now) is not { } delay)
        {
            CompleteWakeup(jobId);
            return DurableJobRunResult.Completed;
        }

        var next = MessagingTime.AddClamped(now, delay > TimeSpan.Zero ? delay : RecheckDelay);
        if (_wakeupJobId is not null && _wakeupJobId != jobId && _wakeupDueTime <= next)
        {
            // 另一份工作首先唤醒 grain ,并重新安排剩余工作的运行时间表。
            CompleteWakeup(jobId);
            return DurableJobRunResult.Completed;
        }

        _wakeupJobId = jobId;
        _wakeupDueTime = next;
        return DurableJobRunResult.RescheduleAt(next);
    }

    private void CompleteWakeup(string jobId)
    {
        if (_wakeupJobId == jobId) _wakeupJobId = null;
        _wakeupEpoch++;
    }

    // 延迟到收件箱、发件箱或登记后醒一醒的下一个工作会停止,或者当无结果时无效。
    private TimeSpan? GetNextWakeupDelay(DateTimeOffset now) =>
        (Inbox.GetNextWakeupDelay(now), _outbox?.GetNextWakeupDelay(now)) switch
        {
            ({ } inbox, { } outbox) => inbox < outbox ? inbox : outbox,
            (var inbox, var outbox) => inbox ?? outbox
        };

    private async Task RepairWakeupAsync()
    {
        try
        {
            await EnsureWakeupAsync(Shared.JobTimeProvider.GetUtcNow(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (!_shutdown.IsCancellationRequested)
        {
            // 恢复工作重新唤醒了剩余工作的警钟。
            Fail(exception, "Scheduling the messaging wake-up failed.");
        }
    }

    private async Task RunAsync()
    {
        // 调用者从 grain 调用和工作处决开始循环; 排队第一个接通到激活
        // 排程器,所以它从来不运行, 和巢内, 呼叫者的堆叠。
        await Task.Yield();
        var cancellationToken = _shutdown.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            SetKeepAlive(Inbox.Count > 0 || _outbox?.Count > 0);
            var pass = _nextPass;
            _nextPass = null;
            var progress = false;
            try
            {
                progress = await Inbox.ProcessReadyAsync(cancellationToken).ConfigureAwait(true);
                if (_outbox is { } outbox) progress |= await outbox.AdvanceAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // 预料的失败在通行证内处理,因此逃避的例外情况使得国家与日记不符。
                progress = false;
                LogPassFailed(Shared.PumpLogger, exception, GrainContext.GrainId);
                Fail(exception, "Messaging processing pass failed.");
            }

            pass?.TrySetResult();
            if (!progress)
            {
                SetKeepAlive(_outbox?.IsDelivering == true);
                await _signal.WaitAsync().ConfigureAwait(true);
            }
        }

        SetKeepAlive(false);
        _nextPass?.TrySetCanceled(cancellationToken);
    }

    private void SetKeepAlive(bool keepAlive)
    {
        if (keepAlive == _keepingAlive || GrainContext is not IActivationKeepAlive activation) return;
        _keepingAlive = keepAlive;
        if (keepAlive)
        {
            activation.AcquireKeepAlive();
        }
        else
        {
            activation.ReleaseKeepAlive();
        }
    }

    public async Task OnStart(CancellationToken cancellationToken)
    {
        if (_outbox is { } outbox) await outbox.StartAsync(cancellationToken).ConfigureAwait(true);
        await Inbox.StartAsync(cancellationToken).ConfigureAwait(true);

        // 恢复的工作在实际时间保持了警醒,即使唤醒这一激活工作已经完成。
        var now = Shared.JobTimeProvider.GetUtcNow();
        if (GetNextWakeupDelay(now) is { } delay)
        {
            await EnsureWakeupAsync(MessagingTime.AddClamped(now, delay), cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 当持久工作重试运行失败的泵工作时返回 。
    /// 工作时钟持久。
    /// </summary>
    /// <remarks>
    /// 工作是 grain唯一能持久地唤醒投入工作的人,因此在经过多次尝试之后放弃
    /// 长时间停电后,如果接到无关的电话,就会重新启用 grain。
    /// </remarks>
    internal static DateTimeOffset GetRetryTime(IJobRunContext context, DateTimeOffset now, TimeSpan initialInterval) =>
        MessagingTime.AddClamped(now, MessagingTime.GetBackoff(initialInterval, context.DequeueCount));

    public async Task OnStop(CancellationToken cancellationToken)
    {
        var scheduling = _scheduling;
        Stop();

        // Grain 呼叫在停止使用寿命周期之前结束, 因此循环和发件箱发送拥有所有剩余文字。
        if (_loop is { } loop)
        {
            await loop.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (scheduling is not null)
        {
            await ((Task)scheduling).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_outbox is { } outbox) await outbox.StopAsync().ConfigureAwait(true);

        // 传送的循环和发送信箱已经结束,所以除非信息失败,否则任何尝试都不会进行。
        await Inbox.RecordCleanStopAsync().ConfigureAwait(true);
    }

    /// <summary>停止此激活的处理。</summary>
    internal void Stop()
    {
        if (_shutdown.IsCancellationRequested) return;
        try
        {
            _shutdown.Cancel();
        }
        catch (AggregateException exception)
        {
            LogCancellationFailed(Shared.PumpLogger, exception);
        }
        finally
        {
            _signal.Signal();
        }
    }

    /// <summary>获取信件是否失败, 所以激活的未承诺更改不能执行 。</summary>
    internal bool IsFailed { get; private set; }

    /// <summary>停止发送信息和停止使用 grain 无法恢复的故障发生后</summary>
    /// <remarks>
    /// 记忆状态可能不再与激活可能发生的行为匹配, 所以以后每期日记都写不出来,
    /// 下一期启动活动从日记恢复。
    /// </remarks>
    internal void Fail(Exception exception, string description)
    {
        IsFailed = true;
        if (_shutdown.IsCancellationRequested) return;
        Stop();
        try
        {
            GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationError, exception, description));
        }
        catch (Exception deactivationException)
        {
            LogDeactivationFailed(Shared.PumpLogger, deactivationException);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Messaging processing pass failed for grain {GrainId}")]
    private static partial void LogPassFailed(ILogger logger, Exception exception, GrainId grainId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Requesting deactivation after a messaging failure failed.")]
    private static partial void LogDeactivationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A messaging cancellation callback failed during shutdown.")]
    private static partial void LogCancellationFailed(ILogger logger, Exception exception);
}
