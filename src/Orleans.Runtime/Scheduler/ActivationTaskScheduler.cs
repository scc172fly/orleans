
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.Scheduler
{
    /// <summary>
    /// 用于按激活实例调度工作的单并发、按序任务调度器。
    /// </summary>
    [DebuggerDisplay("ActivationTaskScheduler RunQueue={workerGroup.ExternalWorkItemCount} GrainContext={workerGroup.GrainContext}")]
    internal sealed partial class ActivationTaskScheduler : TaskScheduler
    {
#if DEBUG
        private readonly ILogger logger;

        private static long idCounter;
        private readonly long myId;
#endif
        private readonly WorkItemGroup workerGroup;
#if EXTRA_STATS
        private readonly CounterStatistic turnsExecutedStatistic;
#endif

        internal ActivationTaskScheduler(WorkItemGroup workGroup)
        {
#if DEBUG
            logger = workGroup.Logger;
            myId = Interlocked.Increment(ref idCounter);
#endif
            workerGroup = workGroup;
#if EXTRA_STATS
            turnsExecutedStatistic = CounterStatistic.FindOrCreate(name + ".TasksExecuted");
#endif
#if DEBUG
            LogCreatedTaskScheduler(this, workerGroup.GrainContext);
#endif
        }

        /// <summary>获取当前调度到此调度器的任务的可枚举集合。</summary>
        /// <returns>当前已调度任务的可枚举集合。</returns>
        protected override IEnumerable<Task> GetScheduledTasks() => this.workerGroup.GetScheduledTasks();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RunTaskFromWorkItemGroup(Task task)
        {
            bool done = TryExecuteTask(task);
            if (!done)
            {
#if DEBUG
                LogWarnTryExecuteTaskNotDone(task.Id, task.Status);
#endif
            }
        }

        /// <summary>将任务加入调度器队列。</summary>
        /// <param name="task">要加入队列的任务。</param>
        protected override void QueueTask(Task task)
        {
#if DEBUG
            LogTraceQueueTask(myId, task.Id);
#endif
            workerGroup.EnqueueTask(task);
        }

        /// <summary>
        /// 确定提供的 <see cref="T:System.Threading.Tasks.Task"/> 是否可以在此次调用中同步执行；若可以，则执行它。
        /// </summary>
        /// <returns>
        /// 指示任务是否已内联执行的布尔值。
        /// </returns>
        /// <param name="task">要执行的 <see cref="T:System.Threading.Tasks.Task"/>。</param>
        /// <param name="taskWasPreviouslyQueued">表示任务此前是否已入队的布尔值。若参数为 True，则任务可能已入队（已调度）；若为 False，则已知任务尚未入队，本次调用旨在不入队的情况下内联执行任务。</param>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            var canExecuteInline = !taskWasPreviouslyQueued && Equals(RuntimeContext.Current, workerGroup.GrainContext);

#if DEBUG
            LogTraceTryExecuteTaskInline(
                myId,
                task.Id,
                task.Status,
                taskWasPreviouslyQueued,
                canExecuteInline,
                workerGroup.ExternalWorkItemCount);
#endif
            if (!canExecuteInline)
            {
#if DEBUG
                LogTraceTryExecuteTaskInlineNotDone(myId, task.Id, task.Status);
#endif
                return false;
            }

#if EXTRA_STATS
            turnsExecutedStatistic.Increment();
#endif
#if DEBUG
            LogTraceTryExecuteTaskInlineYes(myId, task.Id, System.Environment.CurrentManagedThreadId);
#endif
            // 尝试运行任务。
            bool done = TryExecuteTask(task);
#if DEBUG
            if (!done)
            {
                LogWarnTryExecuteTaskNotDone(task.Id, task.Status);
            }

            LogTraceTryExecuteTaskInlineCompleted(myId, task.Id, System.Environment.CurrentManagedThreadId, done);
#endif
            return done;
        }

        public override string ToString()
        {
#if DEBUG
            return $"{GetType().Name}-{myId}:Queued={workerGroup.ExternalWorkItemCount}";
#else
            return $"{GetType().Name}:Queued={workerGroup.ExternalWorkItemCount}";
#endif
        }

#if DEBUG
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Created {TaskScheduler} with GrainContext={GrainContext}"
        )]
        private partial void LogCreatedTaskScheduler(ActivationTaskScheduler taskScheduler, IGrainContext grainContext);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.SchedulerTaskExecuteIncomplete4,
            Message = "RunTask: Incomplete base.TryExecuteTask for Task Id={TaskId} with Status={TaskStatus}"
        )]
        private partial void LogWarnTryExecuteTaskNotDone(int taskId, TaskStatus taskStatus);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{TaskScheduler} QueueTask Task Id={TaskId}"
        )]
        private partial void LogTraceQueueTask(long taskScheduler, int taskId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{TaskScheduler} TryExecuteTaskInline Task Id={TaskId} Status={Status} PreviouslyQueued={PreviouslyQueued} CanExecute={CanExecute} Queued={Queued}"
        )]
        private partial void LogTraceTryExecuteTaskInline(long taskScheduler, int taskId, TaskStatus status, bool previouslyQueued, bool canExecute, int queued);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{TaskScheduler} Completed TryExecuteTaskInline Task Id={TaskId} Status={Status} Execute=No"
        )]
        private partial void LogTraceTryExecuteTaskInlineNotDone(long taskScheduler, int taskId, TaskStatus status);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{TaskScheduler} TryExecuteTaskInline Task Id={TaskId} Thread={Thread} Execute=Yes"
        )]
        private partial void LogTraceTryExecuteTaskInlineYes(long taskScheduler, int taskId, int thread);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{TaskScheduler} Completed TryExecuteTaskInline Task Id={TaskId} Thread={Thread} Execute=Done Ok={Ok}"
        )]
        private partial void LogTraceTryExecuteTaskInlineCompleted(long taskScheduler, int taskId, int thread, bool ok);
#endif
    }
}
