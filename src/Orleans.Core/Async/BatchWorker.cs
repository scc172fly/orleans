using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Timers.Internal;

namespace Orleans
{
    /// <summary>
    /// 异步工作器的通用模式：收到通知时执行工作任务，
    /// 处理队列中的工作。每个工作周期都会处理所有已排队的工作。
    /// 如果在工作周期内有新工作到达，则调度另一个周期。
    /// 工作器在任意时刻最多执行一个工作周期，
    /// 空闲时不消耗资源。它使用 TaskScheduler.Current
    /// 调度工作周期。
    /// </summary>
    public abstract class BatchWorker
    {
        private readonly Lock lockable = new();

        private DateTime? scheduledNotify;

        // 当前工作周期的任务；空闲时为 null。
        private Task? currentWorkCycle;

        // 设置此标志表示任务执行期间有更多工作到达。
        private bool moreWork;

        // 用于向等待者传递下一个工作周期的任务。
        // 仅当存在等待者时，此值才非 null。
        private TaskCompletionSource<Task>? nextWorkCyclePromise;
        private Task? nextWorkCycle;

        /// <summary>在派生类中实现此成员，以定义一个工作周期的具体内容。</summary>
        /// <returns>>
        /// 一个 <see cref="Task"/>。
        /// </returns>
        protected abstract Task Work();

        /// <summary>
        /// 获取或设置用于取消此批处理工作器的取消机制。
        /// </summary>
        protected CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// 通知工作器有更多工作。
        /// </summary>
        public void Notify()
        {
            lock (lockable)
            {
                if (currentWorkCycle != null)
                {
                    // 让当前工作周期知道还有更多工作。
                    moreWork = true;
                }
                else
                {
                    // 启动一个工作周期。
                    Start();
                }
            }
        }

        /// <summary>
        /// 指示批处理工作器重新运行并检查工作，
        /// 但仅当在指定的 <paramref name="utcTime"/> 到来时尚未再次运行。
        /// </summary>
        /// <param name="utcTime"></param>
        public void Notify(DateTime utcTime)
        {
            var now = DateTime.UtcNow;

            if (now >= utcTime)
            {
                Notify();
            }
            else
            {
                lock (lockable)
                {
                    if (!scheduledNotify.HasValue || scheduledNotify.Value > utcTime)
                    {
                        scheduledNotify = utcTime;

                        ScheduleNotify(utcTime, now).Ignore();
                    }
                }
            }
        }

        private async Task ScheduleNotify(DateTime time, DateTime now)
        {
            await TimerManager.Delay(time - now, this.CancellationToken);

            if (scheduledNotify == time)
            {
                Notify();
            }
        }

        private Task Start()
        {
            // 清除已调度的运行。
            scheduledNotify = null;

            // 将执行工作的任务加入队列。
            var task = Task.Factory.StartNew(s => ((BatchWorker)s!).Work(), this, default, default, TaskScheduler.Current).Unwrap();
            currentWorkCycle = task;

            // 在同一个调度器上连接一个用于检查更多工作的延续操作。
            task.ContinueWith(
                (_, s) => ((BatchWorker)s!).CheckForMoreWork(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Current);
            return task;
        }

        /// <summary>
        /// 在每个工作周期结束时，在同一个任务调度器上执行。
        /// </summary>
        private void CheckForMoreWork()
        {
            TaskCompletionSource<Task>? signal;
            Task taskToSignal;

            lock (lockable)
            {
                if (moreWork)
                {
                    moreWork = false;

                    // 检查是否有人创建了用于等待下一个工作周期的任务完成源。
                    // 如果有，则取出并移除它。
                    signal = this.nextWorkCyclePromise;
                    this.nextWorkCyclePromise = null;
                    this.nextWorkCycle = null;

                    // 启动下一个工作周期。
                    taskToSignal = Start();
                }
                else
                {
                    currentWorkCycle = null;
                    return;
                }
            }

            // 为安全起见，必须在此处（锁外）发出信号。
            signal?.SetResult(taskToSignal);
        }

        /// <summary>
        /// 检查此工作器是否处于空闲状态。
        /// </summary>
        public bool IsIdle() => currentWorkCycle == null;

        /// <summary>
        /// 等待当前工作周期完成；如果当前存在尚未处理的工作，也等待下一个周期完成。
        /// </summary>
        public Task WaitForCurrentWorkToBeServiced()
        {
            // 确定具体需要等待什么。
            lock (lockable)
            {
                if (!moreWork)
                {
                    // 只等待当前工作周期。
                    return currentWorkCycle ?? Task.CompletedTask;
                }
                else
                {
                    // 需要等待下一个工作周期。
                    // 但该任务还不存在，因此使用一个在下一个工作周期启动时发出信号的任务完成源。
                    return nextWorkCycle ?? CreateNextWorkCyclePromise();
                }
            }
        }

        private Task CreateNextWorkCyclePromise()
        {
            // 可以同步运行延续操作，因为此任务完成源只会在 CheckForMoreWork 的最后发出完成信号。
            nextWorkCyclePromise = new TaskCompletionSource<Task>();
            return nextWorkCycle = nextWorkCyclePromise.Task.Unwrap();
        }

        /// <summary>
        /// 通知工作器有更多工作，并等待当前工作周期完成；如果当前存在尚未处理的工作，也等待下一个工作周期完成。
        /// </summary>
        public Task NotifyAndWaitForWorkToBeServiced()
        {
            lock (lockable)
            {
                if (currentWorkCycle != null)
                {
                    moreWork = true;
                    return nextWorkCycle ?? CreateNextWorkCyclePromise();
                }
                else
                {
                    return Start();
                }
            }
        }
    }

    /// <summary>
    /// 一种 <see cref="BatchWorker"/> 实现，使用提供的委托来实现 <see cref="BatchWorker.Work"/>。
    /// </summary>
    public class BatchWorkerFromDelegate : BatchWorker
    {
        private readonly Func<Task> work;

        /// <summary>
        /// 初始化新的 <see cref="BatchWorkerFromDelegate"/> 实例。
        /// </summary>
        /// <param name="work">调用 <see cref="BatchWorker.Work"/> 时要执行的委托。</param>
        /// <param name="cancellationToken">用于停止工作器的取消令牌。</param>
        public BatchWorkerFromDelegate(Func<Task> work, CancellationToken cancellationToken = default)
        {
            this.work = work;
            this.CancellationToken = cancellationToken;
        }

        /// <inheritdoc />
        protected override Task Work()
        {
            return work();
        }
    }
}
