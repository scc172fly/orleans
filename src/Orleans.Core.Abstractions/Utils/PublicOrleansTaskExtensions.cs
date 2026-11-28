using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 用于处理 <see cref="Task"/> 实例的实用方法。
    /// </summary>
    public static class PublicOrleansTaskExtensions
    {
        private static readonly Action<Task> IgnoreTaskContinuation = t => { _ = t.Exception; };

        /// <summary>
        /// 观察并忽略指定 Task 中可能出现的异常。
        /// 如果 Task 失败并抛出始终未被观察到的异常，.NET 终结器线程将捕获它。
        /// 此函数等待指定任务；如果抛出异常，则观察并直接忽略该异常。
        /// 这可以避免异常升级至 .NET 终结器线程。
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
