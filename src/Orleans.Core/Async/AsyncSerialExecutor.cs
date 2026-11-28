using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 用于串行执行异步函数的工具类。
    /// 可以在可重入 Grain 代码中使用它，以不可重入（串行）的方式执行某些方法。
    /// </summary>
    /// <typeparam name="TResult">
    /// 此执行器调用的函数所返回的底层类型。
    /// </typeparam>
    public class AsyncSerialExecutor<TResult>
    {
        private readonly ConcurrentQueue<Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>> actions = new ConcurrentQueue<Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>>();
        private readonly InterlockedExchangeLock locker = new InterlockedExchangeLock();

        /// <summary>
        /// 依赖 <see cref="Interlocked.Exchange(ref long, long)"/> 的锁。
        /// </summary>
        private class InterlockedExchangeLock
        {
            private const int Locked = 1;
            private const int Unlocked = 0;
            private int lockState = Unlocked;

            /// <summary>
            /// 尝试获取锁，成功时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
            /// </summary>
            /// <returns>
            /// 获取锁成功时为 <see langword="true"/>，否则为 <see langword="false"/>。
            /// </returns>
            public bool TryGetLock()
            {
                return Interlocked.Exchange(ref lockState, Locked) != Locked;
            }

            /// <summary>
            /// 无条件释放锁。
            /// </summary>
            public void ReleaseLock()
            {
                Interlocked.Exchange(ref lockState, Unlocked);
            }
        }

        /// <summary>
        /// 提交下一个待执行函数。它会在此前提交的所有函数完成后执行，执行过程不会交错。
        /// 返回表示给定函数执行过程的任务。
        /// 给定函数执行完毕后，返回的任务将完成。
        /// </summary>
        /// <param name="func">要调度执行的函数。</param>
        /// <returns>已调度函数的调用结果。</returns>
        public Task<TResult> AddNext(Func<Task<TResult>> func)
        {
            var resolver = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            actions.Enqueue(new Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>(resolver, func));
            Task<TResult> task = resolver.Task;
            ExecuteNext().Ignore();
            return task;
        }

        /// <summary>
        /// 执行下一个已调度的函数。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        private async Task ExecuteNext()
        {
            while (!actions.IsEmpty)
            {
                bool gotLock = false;
                try
                {
                    if (!(gotLock = locker.TryGetLock()))
                    {
                        return;
                    }

                    while (!actions.IsEmpty)
                    {
                        Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>? actionTuple;
                        if (actions.TryDequeue(out actionTuple))
                        {
                            try
                            {
                                TResult result = await actionTuple.Item2();
                                actionTuple.Item1.TrySetResult(result);
                            }
                            catch (Exception exc)
                            {
                                actionTuple.Item1.TrySetException(exc);
                            }
                        }
                    }
                }
                finally
                {
                    if (gotLock)
                        locker.ReleaseLock();
                }
            }
        }
    }

    /// <summary>
    /// 用于串行执行异步函数的工具类。
    /// 可以在可重入 Grain 代码中使用它，以不可重入（串行）的方式执行某些方法。
    /// </summary>
    public class AsyncSerialExecutor
    {
        private readonly AsyncSerialExecutor<bool> executor = new AsyncSerialExecutor<bool>();

        /// <summary>
        /// 提交下一个待执行函数。它会在此前提交的所有函数完成后执行，执行过程不会交错。
        /// 返回表示给定函数执行过程的任务。
        /// 给定函数执行完毕后，返回的任务将完成。
        /// </summary>
        /// <param name="func">要调度执行的函数。</param>
        /// <returns>已调度函数的调用结果。</returns>
        public Task AddNext(Func<Task> func)
        {
            return this.executor.AddNext(() => Wrap(func));
        }

        private static async Task<bool> Wrap(Func<Task> func)
        {
            await func();
            return true;
        }
    }
}
