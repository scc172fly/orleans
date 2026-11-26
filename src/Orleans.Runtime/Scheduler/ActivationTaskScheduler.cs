
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.Scheduler
{
    /// <summary>
    /// 单币、单顺序任务调度器,用于每项活动工作时间安排。
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

        /// <summary>获取一个包含当前排程器上所安排任务数量的清单。</summary>
        /// <returns>目前计划的任务中可列成数。</returns>
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

        /// <summary>将任务排队到调度器。</summary>
        /// <param name="task">要排队的任务。</param>
        protected override void QueueTask(Task task)
        {
#if DEBUG
            LogTraceQueueTask(myId, task.Id);
#endif
            workerGroup.EnqueueTask(task);
        }

        /// <summary>
        /// 确定是否在此调用中同步执行所提供的 <see cref="T:System.Threading.Tasks.Task"/> ,如果可以,则执行。
        /// </summary>
        /// <returns>
        /// 显示任务是否内嵌执行的布尔值 。
        /// </returns>
        /// <param name="task">下 <see cref="T:System.Threading.Tasks.Task"/> 待执行。</param>
        /// <param name="taskWasPreviouslyQueued">布尔表示任务是否先前被排队。 如果此参数是真实的, 那么任务可能已经预先排队( 计划) ; 如果假, 那么任务没有排队, 而这个调用是为了在不排队的情况下执行内线任务 。</param>
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
            // 尝试运行任务 。
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
