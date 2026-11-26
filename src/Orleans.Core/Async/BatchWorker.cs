using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Timers.Internal;

namespace Orleans
{
    /// <summary>
    /// 从事某项工作任务的非同步工人在接到通知后的一般模式,
    /// 用于服务排队工作。每个工作周期处理所有排队工作。 
    /// 如果在工作周期内新工作到来,则安排另一个周期。 
    /// 工人一次执行的工作周期从未超过一个实例, 
    /// 闲置时不消耗资源,而是使用 TaskScheduler.Current 
    /// 以安排工作周期。
    /// </summary>
    public abstract class BatchWorker
    {
        private readonly Lock lockable = new();

        private DateTime? scheduledNotify;

        // 当前工作周期的任务, 即空时无效
        private Task? currentWorkCycle;

        // 设置的旗帜表示在执行任务期间已完成更多工作
        private bool moreWork;

        // 用于向服务生传达下一个工作周期的任务。
        // 只有有服务生时, 此值才为非无效值 。
        private TaskCompletionSource<Task>? nextWorkCyclePromise;
        private Task? nextWorkCycle;

        /// <summary>在衍生类别中执行该成员,以界定什么构成工作周期</summary>
        /// <returns>>
        /// A <see cref="Task"/>
        /// </returns>
        protected abstract Task Work();

        /// <summary>
        /// 获取或设置取消该批量工人的取消时所用的取消 。
        /// </summary>
        protected CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// 通知工人还有更多工作。
        /// </summary>
        public void Notify()
        {
            lock (lockable)
            {
                if (currentWorkCycle != null)
                {
                    // 让当前工作周期知道还有更多工作要做
                    moreWork = true;
                }
                else
                {
                    // 开始工作周期
                    Start();
                }
            }
        }

        /// <summary>
        /// 指示批批工人再次跑去检查工作,如果
        /// 此时它尚未在指定 <paramref name="utcTime"/>时再次运行。
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
            // 清除任何计划运行
            scheduledNotify = null;

            // 正在做的工作任务排队
            var task = Task.Factory.StartNew(s => ((BatchWorker)s!).Work(), this, default, default, TaskScheduler.Current).Unwrap();
            currentWorkCycle = task;

            // 同一排程器上检查更多工作的链条的连续链
            task.ContinueWith(
                (_, s) => ((BatchWorker)s!).CheckForMoreWork(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Current);
            return task;
        }

        /// <summary>
        /// 在每个工作周期结束时执行同一任务调度器。
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

                    // 看看是否有人创造了一个希望 等待下一个工作周期
                    // 如果有,拿取并移走
                    signal = this.nextWorkCyclePromise;
                    this.nextWorkCyclePromise = null;
                    this.nextWorkCycle = null;

                    // 开始下一个工作周期
                    taskToSignal = Start();
                }
                else
                {
                    currentWorkCycle = null;
                    return;
                }
            }

            // 安全起见,必须在这里发出信号 所以它不会在锁下
            signal?.SetResult(taskToSignal);
        }

        /// <summary>
        /// 检查该工人是否闲置。
        /// </summary>
        public bool IsIdle() => currentWorkCycle == null;

        /// <summary>
        /// 等待目前的工作周期,以及如果目前没有服务的工作,则等待下一个工作周期。
        /// </summary>
        public Task WaitForCurrentWorkToBeServiced()
        {
            // 找出我们到底需要等待什么
            lock (lockable)
            {
                if (!moreWork)
                {
                    // 等待当前工作周期
                    return currentWorkCycle ?? Task.CompletedTask;
                }
                else
                {
                    // 我们需要等待下一个工作周期
                    // 但这项任务还不存在,所以我们使用一个承诺,即下一个工作周期启动时发出信号
                    return nextWorkCycle ?? CreateNextWorkCyclePromise();
                }
            }
        }

        private Task CreateNextWorkCyclePromise()
        {
            // 同步运行任何后续数据是可以的, 因为这个承诺只在 CheckForMoreWork结束时才发出信号
            nextWorkCyclePromise = new TaskCompletionSource<Task>();
            return nextWorkCycle = nextWorkCyclePromise.Task.Unwrap();
        }

        /// <summary>
        /// 通知工人还有更多的工作,等待目前的工作周期,如果目前没有服务的工作,还要等待下一个工作周期。
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
    /// 执行 <see cref="BatchWorker"/> ,作为执行 <see cref="BatchWorker.Work"/> ,执行指定的代表。
    /// </summary>
    public class BatchWorkerFromDelegate : BatchWorker
    {
        private readonly Func<Task> work;

        /// <summary>
        /// 初始化一个新的 <see cref="BatchWorkerFromDelegate"/> 实例。
        /// </summary>
        /// <param name="work">代表援引时的援引 <see cref="BatchWorker.Work"/> 援引。</param>
        /// <param name="cancellationToken">用于阻止工人的取消牌。</param>
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
