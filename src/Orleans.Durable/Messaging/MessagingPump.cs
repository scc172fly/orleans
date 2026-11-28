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
/// 驱动单个激活实例的收件箱处理与发件箱投递。
/// </summary>
/// <remarks>
/// 单一循环在激活实例调度器上运行，每当收到信号时处理两个队列。
/// 只要任一队列包含激活实例可执行的工作，或通过
/// <see cref="IInbox.RegisterWakeup"/> 注册的回调报告了工作，就由一个持久化作业持续唤醒 Grain；收件箱消息等待处理器时不安排唤醒。
/// 如果暂存的工作早于当前唤醒时间到期，则安排更早的唤醒；被较新且
/// 更早的唤醒替代的作业会在运行时完成。
/// 唤醒可能提前、延迟或重复：每轮均依据当前状态确定要执行的工作。
/// 唤醒是针对崩溃的保障机制；在线循环收到信号便立即执行工作，因此已到期的工作
/// 需要在 <see cref="RecheckDelay"/> 内安排唤醒，并且仅当没有已跟踪或正在调度的作业能在该时间之前到期时，
/// 写入才会调度作业。正在运行的作业每次执行时重新安排一次唤醒，因此繁忙的 Grain 不必在每次写入时
/// 执行作业操作。激活实例失败时，该作业无限期重试（参见 <see cref="GetRetryTime"/>），且每个
/// 激活实例都会依据恢复的状态重新安排唤醒，因此已提交的工作始终保有唤醒机制。
/// 唤醒记账在激活实例调度器上运行：日志写入在其上进行准备，并与作业运行串行执行。
/// 循环在处理排队工作及发件箱投递处于在途状态时保持激活实例存活。
/// 之后才到期的工作依赖持久化作业，因此激活实例在等待期间可以被回收。
/// </remarks>
internal sealed partial class MessagingPump : IDurableJobFeatureHandler, ILifecycleObserver
{
    internal const string JobName = "orleans.messaging.pump";
    internal static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(1);

    // 公开特性暴露运行时的内部密封策略类型。
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

    // 下一次唤醒 Grain 的作业（如有）及其到期时间。作业运行完成时清除此记录。
    private string? _wakeupJobId;
    private DateTimeOffset _wakeupDueTime;

    // 每次作业运行完成时递增。较早世代中请求的调度可能属于已经
    // 运行并完成的作业，因此不再保证唤醒。
    private int _wakeupEpoch;
    private int _schedulingEpoch;

    public MessagingPump(IGrainContext grainContext, IJournaledStateManager stateManager, IDurableJobHandlerRegistry jobHandlers, MessagingShared shared)
    {
        // 消息传递要求每个 Grain 标识仅有一个激活实例，因此无状态工作者的每个激活实例都会失败。
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

        // 到期工作的唤醒最多延迟 RecheckDelay 才触发，因此循环在激活完成后启动并执行
        // 已恢复和新暂存的工作，无需等待唤醒。
        grainContext.ObservableLifecycle.Subscribe(nameof(MessagingPump), GrainLifecycleStage.Last, _ =>
        {
            EnsureStarted();
            return Task.CompletedTask;
        }, static _ => Task.CompletedTask);

        // 恢复期间在 SetupState 阶段报告队列深度，因此在该阶段安排释放，可涵盖 Activate 之前失败的激活实例。
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

    /// <summary>获取此激活实例的 Grain 类型指标标签。</summary>
    public string GrainTypeTag { get; }

    /// <summary>获取此激活实例的消息传递停止时被取消的令牌。</summary>
    public CancellationToken ShutdownToken => _shutdown.Token;

    internal void Register(Inbox inbox) => _inbox = inbox;

    internal void Register(Outbox outbox) => _outbox = outbox;

    internal Inbox Inbox => _inbox ?? throw new InvalidOperationException("The messaging inbox has not been registered.");


    public bool CanHandle(string jobName) => string.Equals(jobName, JobName, StringComparison.Ordinal);

    /// <summary>请求进行一轮处理。</summary>
    public void Signal()
    {
        if (!_shutdown.IsCancellationRequested) _signal.Signal();
    }

    /// <summary>请求进行一轮处理，并确保已变为可处理的工作有持久化唤醒。</summary>
    public void RequestWakeup()
    {
        if (_shutdown.IsCancellationRequested) return;
        _signal.Signal();
        RepairWakeupAsync().Ignore();
    }

    /// <summary>激活完成并开始处理 Grain 调用时启动循环。</summary>
    public void EnsureStarted()
    {
        if (_loop is null && !_shutdown.IsCancellationRequested)
        {
            _loop = RunAsync();
        }
    }

    /// <summary>
    /// 确保持久化作业最迟在 <paramref name="dueTime"/> 唤醒此 Grain，或对于更早到期的工作，在
    /// <see cref="RecheckDelay"/> 内唤醒，时间均依据 Durable Jobs 时钟。
    /// </summary>
    public async ValueTask EnsureWakeupAsync(DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        // 在线循环在工作到期后立即处理，因此只要在 RecheckDelay 内有已跟踪或正在调度的唤醒，
        // 即可在激活实例失败时保障该工作。
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
        // 正在运行的作业不断重新调度自身，直到两个队列及每个已注册唤醒均报告没有工作。
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
            // 失败的处理轮次可能只留下暂存的移除操作；其作业必须在激活实例失败后继续存在。
            _shutdown.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            // Durable Jobs 重试失败的运行（参见 GetRetryTime），从而唤醒恢复后的激活实例。
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
            // 另一个作业先唤醒 Grain，其运行会针对剩余工作重新调度。
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

    // 距收件箱、发件箱或已注册唤醒下一次出现工作的延迟；均无工作时为 null。
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
            // 恢复为剩余工作重新建立唤醒机制。
            Fail(exception, "Scheduling the messaging wake-up failed.");
        }
    }

    private async Task RunAsync()
    {
        // 调用方从 Grain 调用和作业执行中启动循环；将首次处理排入激活实例的
        // 调度器，以便它绝不在调用方的调用栈上执行，也不嵌套在该调用栈内。
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
                // 预期的失败在处理轮次内部处理，因此未被捕获的异常会使状态与日志不一致。
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

        // 已恢复的工作在其真实到期时间保有唤醒，即使唤醒当前激活实例的作业已完成也是如此。
        var now = Shared.JobTimeProvider.GetUtcNow();
        if (GetNextWakeupDelay(now) is { } delay)
        {
            await EnsureWakeupAsync(MessagingTime.AddClamped(now, delay), cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 返回 Durable Jobs 对运行失败的消息泵作业进行重试的时间；重试依据
    /// Durable Jobs 时钟无限期进行并采用退避策略。
    /// </summary>
    /// <remarks>
    /// 该作业是 Grain 针对已提交工作的唯一持久化唤醒机制，因此若在有限次数的尝试后放弃，
    /// 长时间中断后这些工作将无法继续，直至某个无关调用重新激活 Grain。
    /// </remarks>
    internal static DateTimeOffset GetRetryTime(IJobRunContext context, DateTimeOffset now, TimeSpan initialInterval) =>
        MessagingTime.AddClamped(now, MessagingTime.GetBackoff(initialInterval, context.DequeueCount));

    public async Task OnStop(CancellationToken cancellationToken)
    {
        var scheduling = _scheduling;
        Stop();

        // Grain 调用会在停用结束生命周期之前完成，因此循环与发件箱投递负责所有剩余写入。
        if (_loop is { } loop)
        {
            await loop.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (scheduling is not null)
        {
            await ((Task)scheduling).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_outbox is { } outbox) await outbox.StopAsync().ConfigureAwait(true);

        // 循环及发件箱的投递均已结束，因此除非消息传递失败，否则没有正在进行的尝试。
        await Inbox.RecordCleanStopAsync().ConfigureAwait(true);
    }

    /// <summary>停止此激活实例的处理。</summary>
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

    /// <summary>获取消息传递是否失败；若失败，则不得提交激活实例尚未提交的变更。</summary>
    internal bool IsFailed { get; private set; }

    /// <summary>发生不可恢复的失败后停止消息传递并停用 Grain。</summary>
    /// <remarks>
    /// 内存状态可能不再符合激活实例允许提交的状态，因此后续每次日志写入均失败，
    /// 下一激活实例从日志恢复。
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
