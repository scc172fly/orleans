
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime.Scheduler;

[DebuggerDisplay("WorkItemGroup Context={GrainContext} State={_state}")]
internal sealed partial class WorkItemGroup : IThreadPoolWorkItem, IWorkItemScheduler
{
    private enum WorkGroupStatus : byte
    {
        Waiting = 0,
        Runnable = 1,
        Running = 2
    }

    private readonly Lock _lockObj = new();
    private readonly Queue<Task> _workItems = new();
    private readonly SchedulingOptions _schedulingOptions;
    private readonly SchedulerInstruments _schedulerInstruments;

    private long _totalItemsEnqueued;
    private long _totalItemsProcessed;
    private long _lastLongQueueWarningTimestamp;

    private WorkGroupStatus _state;
    private Task? _currentTask;
    private long _currentTaskStarted;

    internal ActivationTaskScheduler TaskScheduler { get; }

    public IGrainContext GrainContext { get; set; }

    internal ILogger Logger => GrainContext switch
    {
        ActivationData activation => activation.Shared.SchedulerLogger,
        SystemTarget systemTarget => systemTarget.SchedulerLogger,
        _ => GrainContext.ActivationServices.GetRequiredService<ILogger<WorkItemGroup>>()
    };

    internal int ExternalWorkItemCount
    {
        get { lock (_lockObj) { return _workItems.Count; } }
    }

    public WorkItemGroup(
        IGrainContext grainContext,
        IOptions<SchedulingOptions> schedulingOptions,
        SchedulerInstruments schedulerInstruments)
    {
        ArgumentNullException.ThrowIfNull(grainContext);
        GrainContext = grainContext;
        _schedulingOptions = schedulingOptions.Value;
        _schedulerInstruments = schedulerInstruments;
        _state = WorkGroupStatus.Waiting;
        TaskScheduler = new ActivationTaskScheduler(this);
    }

    /// <summary>
    /// 向此激活实例添加任务。
    /// 如果将任务添加到运行列表且之前处于等待状态，则现在进入可运行状态。
    /// </summary>
    /// <param name="task">要添加的工作项。</param>
    public void EnqueueTask(Task task)
    {
#if DEBUG
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Trace))
        {
            LogTraceEnqueueWorkItem(logger, task.ToString()!, GrainContext, System.Threading.Tasks.TaskScheduler.Current);
        }
#endif

        lock (_lockObj)
        {
            long thisSequenceNumber = _totalItemsEnqueued++;
            int count = _workItems.Count;

            _workItems.Enqueue(task);
            int maxPendingItemsLimit = _schedulingOptions.MaxPendingWorkItemsSoftLimit;
            if (maxPendingItemsLimit > 0 && count > maxPendingItemsLimit)
            {
                var now = Environment.TickCount64;
                if (now > _lastLongQueueWarningTimestamp + 10_000)
                {
                    LogTooManyTasksInQueue(count, maxPendingItemsLimit);
                }

                _lastLongQueueWarningTimestamp = now;
            }

            if (_state != WorkGroupStatus.Waiting)
            {
                return;
            }

            _state = WorkGroupStatus.Runnable;
#if DEBUG
            var runQueueLogger = Logger;
            if (runQueueLogger.IsEnabled(LogLevel.Trace))
            {
                LogTraceAddToRunQueue(runQueueLogger, task.ToString()!, thisSequenceNumber, GrainContext);
            }
#endif
            ScheduleExecution(this);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogTooManyTasksInQueue(int count, int maxPendingItemsLimit)
    {
        LogWarningTooManyPendingItems(Logger, count, GrainContext?.ToString() ?? "Unknown", maxPendingItemsLimit);
    }

    /// <summary>
    /// 仅供调试器使用。
    /// </summary>
    internal IEnumerable<Task> GetScheduledTasks()
    {
        foreach (var task in _workItems)
        {
            yield return task;
        }
    }

    // 为此激活实例执行一个或多个执行轮次。
    // 此方法始终在单线程环境中调用，也就是说，任意时刻最多有一个
    // 线程处于此方法中，但其他异步线程仍可能向队列中添加任务等。
    public void Execute()
    {
        RuntimeContext.SetExecutionContext(GrainContext, out var originalContext);
        var turnWarningDurationMs = (long)Math.Ceiling(_schedulingOptions.TurnWarningLengthThreshold.TotalMilliseconds);
        var activationSchedulingQuantumMs = (long)_schedulingOptions.ActivationSchedulingQuantum.TotalMilliseconds;
        try
        {

            // 处理多个工作项：排空此激活实例的队列（不超过最大工作项数）。
            long loopStart, taskStart, taskEnd;
            loopStart = taskStart = taskEnd = Environment.TickCount64;
            do
            {
                Task task;
                lock (_lockObj)
                {
                    _state = WorkGroupStatus.Running;

                    // 获取列表中的第一个工作项。
                    if (_workItems.Count > 0)
                    {
                        _currentTask = task = _workItems.Dequeue();
                        _currentTaskStarted = taskStart;
                    }
                    else
                    {
                        // 如果列表为空，则处理完毕。
                        break;
                    }
                }

#if DEBUG
                LogTaskStart(task);
#endif
                try
                {
                    TaskScheduler.RunTaskFromWorkItemGroup(task);
                }
                finally
                {
                    _totalItemsProcessed++;
                    taskEnd = Environment.TickCount64;
                    var taskDurationMs = taskEnd - taskStart;
                    taskStart = taskEnd;
                    if (taskDurationMs > turnWarningDurationMs)
                    {
                        _schedulerInstruments.OnLongRunningTurn();
                        LogLongRunningTurn(task, taskDurationMs);
                    }

                    _currentTask = null;
                }
            }
            while (activationSchedulingQuantumMs <= 0 || taskEnd - loopStart < activationSchedulingQuantumMs);
        }
        catch (Exception ex)
        {
            LogTaskLoopError(ex);
        }
        finally
        {
            // 现在已不再处于运行状态。
            // 如果运行列表中仍有工作项，则处于可运行状态，需要重新加入 Silo 运行队列；
            // 如果运行列表为空，则处于等待状态。
            lock (_lockObj)
            {
                if (_workItems.Count > 0)
                {
                    _state = WorkGroupStatus.Runnable;
                    ScheduleExecution(this);
                }
                else
                {
                    _state = WorkGroupStatus.Waiting;
                }
            }

            RuntimeContext.ResetExecutionContext(originalContext);
        }
    }

#if DEBUG
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogTaskStart(Task task)
    {
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Trace))
        {
            LogTraceAboutToExecuteTask(logger, task.ToString()!, GrainContext);
        }
    }
#endif

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogTaskLoopError(Exception ex)
    {
        LogErrorTaskLoop(Logger, ex, Environment.CurrentManagedThreadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogLongRunningTurn(Task task, long taskDurationMs)
    {
        var logger = Logger;
        if (Debugger.IsAttached || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        var taskDuration = TimeSpan.FromMilliseconds(taskDurationMs);
        LogWarningLongRunningTurn(
            logger,
            (task.AsyncState ?? task).ToString()!,
            GrainContext?.ToString() ?? "Unknown",
            taskDuration.ToString("g"),
            _schedulingOptions.TurnWarningLengthThreshold,
            Environment.CurrentManagedThreadId.ToString());
    }

    public override string ToString() => $"{(GrainContext is SystemTarget ? "System*" : "")}WorkItemGroup:Name={GrainContext?.ToString() ?? "Unknown"},WorkGroupStatus={_state}";

    public string DumpStatus()
    {
        lock (_lockObj)
        {
            var sb = new StringBuilder();
            sb.Append(this);
            sb.AppendFormat(". Currently QueuedWorkItems={0}; Total Enqueued={1}; Total processed={2}; ",
                _workItems.Count, _totalItemsEnqueued, _totalItemsProcessed);
            if (_currentTask is Task task)
            {
                sb.AppendFormat(" Executing Task Id={0} Status={1} for {2}.",
                    task.Id, task.Status, TimeSpan.FromMilliseconds(Environment.TickCount64 - _currentTaskStarted));
            }

            sb.AppendFormat("TaskRunner={0}; ", TaskScheduler);
            if (GrainContext != null)
            {
                var detailedStatus = GrainContext switch
                {
                    ActivationData activationData => activationData.ToDetailedString(includeExtraDetails: true),
                    SystemTarget systemTarget => systemTarget.ToDetailedString(),
                    object obj => obj.ToString(),
                    _ => "None"
                };
                sb.AppendFormat("Detailed context=<{0}>", detailedStatus);
            }
            return sb.ToString();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ScheduleExecution(WorkItemGroup workItem) => ThreadPool.UnsafeQueueUserWorkItem(workItem, preferLocal: true);

    public void QueueAction(Action action) => TaskScheduler.QueueAction(action);
    public void QueueAction(Action<object> action, object state) => TaskScheduler.QueueAction(action, state);
    public void QueueTask(Task task) => task.Start(TaskScheduler);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EnqueueWorkItem {Task} into {GrainContext} when TaskScheduler.Current={TaskScheduler}"
    )]
    private static partial void LogTraceEnqueueWorkItem(ILogger logger, string task, IGrainContext grainContext, TaskScheduler taskScheduler);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Add to RunQueue {Task}, #{SequenceNumber}, onto {GrainContext}"
    )]
    private static partial void LogTraceAddToRunQueue(ILogger logger, string task, long sequenceNumber, IGrainContext grainContext);

    [LoggerMessage(
        EventId = (int)ErrorCode.SchedulerTooManyPendingItems,
        Level = LogLevel.Warning,
        Message = "{PendingWorkItemCount} pending work items for group {WorkGroupName}, exceeding the warning threshold of {WarningThreshold}"
    )]
    private static partial void LogWarningTooManyPendingItems(ILogger logger, int pendingWorkItemCount, string workGroupName, int warningThreshold);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "About to execute task '{Task}' in GrainContext={GrainContext}"
    )]
    private static partial void LogTraceAboutToExecuteTask(ILogger logger, string task, IGrainContext grainContext);

    [LoggerMessage(
        EventId = (int)ErrorCode.Runtime_Error_100032,
        Level = LogLevel.Error,
        Message = "Worker thread {Thread} caught an exception thrown from IWorkItem.Execute"
    )]
    private static partial void LogErrorTaskLoop(ILogger logger, Exception exception, int thread);

    [LoggerMessage(
        EventId = (int)ErrorCode.SchedulerTurnTooLong3,
        Level = LogLevel.Warning,
        Message = "Task {Task} in WorkGroup {GrainContext} took elapsed time {Duration} for execution, which is longer than {TurnWarningLengthThreshold}. Running on thread {Thread}"
    )]
    private static partial void LogWarningLongRunningTurn(ILogger logger, string task, string grainContext, string duration, TimeSpan turnWarningLengthThreshold, string thread);
}
