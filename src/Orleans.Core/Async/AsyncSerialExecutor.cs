using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 提供连续执行同步函数的通用类。
    /// 可在重录代码 grain 内用于以非重录(序列)方式执行某些方法。
    /// </summary>
    /// <typeparam name="TResult">
    /// 根由此执行器引用的函数返回的底型 。
    /// </typeparam>
    public class AsyncSerialExecutor<TResult>
    {
        private readonly ConcurrentQueue<Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>> actions = new ConcurrentQueue<Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>>();
        private readonly InterlockedExchangeLock locker = new InterlockedExchangeLock();

        /// <summary>
        /// 依赖 <see cref="Interlocked.Exchange(ref long, long)"/>的锁定
        /// </summary>
        private class InterlockedExchangeLock
        {
            private const int Locked = 1;
            private const int Unlocked = 0;
            private int lockState = Unlocked;

            /// <summary>
            /// 试图获取锁,如果获得锁,则返回 <see langword="true"/> ,否则返回 <see langword="false"/> 。
            /// </summary>
            /// <returns>
            /// <see langword="true"/> ,如果锁定是购置的, <see langword="false"/> 以其他方式。
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
        /// 提交下一个函数以供执行。它将在以前提交的所有职能完成后执行,而不中断处决。
        /// 返回代表执行此指定函数的许诺。 
        /// 返回的保证将在特定职能完成后解决。
        /// </summary>
        /// <param name="func">用于指定援引的函数。</param>
        /// <returns>计划函数调用的结果。</returns>
        public Task<TResult> AddNext(Func<Task<TResult>> func)
        {
            var resolver = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            actions.Enqueue(new Tuple<TaskCompletionSource<TResult>, Func<Task<TResult>>>(resolver, func));
            Task<TResult> task = resolver.Task;
            ExecuteNext().Ignore();
            return task;
        }

        /// <summary>
        /// 执行下个计划函数。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
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
    /// 提供连续执行同步函数的通用类。
    /// 可在重录代码 grain 内用于以非重录(序列)方式执行某些方法。
    /// </summary>
    public class AsyncSerialExecutor
    {
        private readonly AsyncSerialExecutor<bool> executor = new AsyncSerialExecutor<bool>();

        /// <summary>
        /// 提交下一个函数以供执行。它将在以前提交的所有功能完成后执行,而不中断处决。
        /// 返回代表执行此指定函数的许诺。 
        /// 返回的保证将在特定职能完成后解决。
        /// </summary>
        /// <param name="func">用于指定援引的函数。</param>
        /// <returns>计划函数调用的结果。</returns>
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
