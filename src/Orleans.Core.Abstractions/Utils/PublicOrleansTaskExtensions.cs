using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 处理 <see cref="Task"/> 实例的实用功能。
    /// </summary>
    public static class PublicOrleansTaskExtensions
    {
        private static readonly Action<Task> IgnoreTaskContinuation = t => { _ = t.Exception; };

        /// <summary>
        /// 观察并忽略了特定任务的潜在例外。
        /// 如果任务失败并丢弃一个从未观察到的例外, 它将被 .NET 最终线索所抓住 。
        /// 3⁄4 ̄ ̧漯B
        /// 这将防止这一例外升级为 .NET 最后线索。
        /// </summary>
        /// <param name="task">要忽略的任务。</param>
        public static void Ignore(this Task task)
        {
            ArgumentNullException.ThrowIfNull(task);

            if (task.IsCompleted)
            {
                _ = task.Exception;
            }
            else
            {
                task.ContinueWith(
                    IgnoreTaskContinuation,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
