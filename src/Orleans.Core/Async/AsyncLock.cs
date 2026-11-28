using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 一种异步互斥机制，支持通过 using 限定作用域。
    /// </summary>
    /// <remarks>
    /// （改编自 http://blogs.msdn.com/b/pfxteam/archive/2012/02/12/10266988.aspx）
    ///
    /// 使用 <b>async</b> 编程时，<b>lock</b> 关键字存在以下问题：
    /// <list type="bullet">
    /// <item><description><b>lock</b> 会阻塞线程，使其等待对代码临界区的独占访问权限。</description></item>
    /// <item><description>在 <b>lock</b> 语句的作用域内不能使用 <b>await</b> 关键字。</description></item>
    /// </list>
    ///
    /// 有时仍然需要独占访问代码临界区。AsyncLock 提供
    /// 与（不可递归获取的）互斥锁相对应的语义，同时符合异步编程原则。
    /// </remarks>
    /// <example>
    /// 以下示例实现了一项需要在持有锁时执行的工作：
    /// <code>
    /// class Test
    /// {
    ///     private AsyncLock _initLock = new AsyncLock();
    ///     public async Task&lt;int> WorkUnderLock()
    ///     {
    ///             using (await _initLock.LockAsync()) // analogous to lock(_initLock)
    ///             {
    ///                  return await DoSomeWork();
    ///             }
    ///     }
    /// }
    /// </code>
    /// </example>
    ///
    /// 我们决定保持实现简单，并尽可能模拟普通互斥锁的语义。
    /// 1) AsyncLock 不实现 IDisposable，因为我们不希望开发者
    /// 在仍有未释放的 LockReleaser 时错误地手动释放锁。
    /// 2) AsyncLock 无须实现终结器。SemaphoreSlim 的底层资源最终会由 .NET
    /// 在 SemaphoreSlim 终结时释放。为 AsyncLock 添加终结器并不会加速这一过程。
    /// 3) LockReleaser 实现 IDisposable，以支持 using 模式。
    /// 4) LockReleaser 无须实现终结器。如果用户忘记对 LockReleaser 调用 Dispose（类似于忘记释放互斥锁），
    /// AsyncLock 将保持锁定，可能导致死锁。这是可以接受的，因为它与普通互斥锁的语义完全一致：忘记解锁时，互斥锁仍然保持锁定。
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "AsyncLock has mutex lifetime semantics: the semaphore remains available for every outstanding releaser and is reclaimed with the lock.")]
    internal class AsyncLock
    {
        private readonly SemaphoreSlim semaphore;

        /// <summary>
        /// 初始化 <see cref="AsyncLock"/> 类的新实例。
        /// </summary>
        public AsyncLock()
        {
            semaphore = new SemaphoreSlim(1);
        }

        /// <summary>
        /// 异步获取锁。
        /// </summary>
        /// <returns>一个必须用于释放锁的 <see cref="IDisposable"/>。</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of each LockReleaser is transferred to the caller through the returned ValueTask.")]
        public ValueTask<IDisposable> LockAsync()
        {
            Task wait = semaphore.WaitAsync();
            if (wait.IsCompletedSuccessfully)
            {
                return new(new LockReleaser(this));
            }
            else
            {
                return LockAsyncInternal(this, wait);

                static async ValueTask<IDisposable> LockAsyncInternal(AsyncLock self, Task waitTask)
                {
                    await waitTask.ConfigureAwait(false);
                    return new LockReleaser(self);
                }
            }
        }

        private class LockReleaser : IDisposable
        {
            private AsyncLock? target;

            internal LockReleaser(AsyncLock target)
            {
                this.target = target;
            }

            public void Dispose()
            {
                if (target == null)
                    return;

                // 先将引用设为 null，再调用 Release，这样即使 Release 抛出异常，也不会继续持有该引用。
                AsyncLock tmp = target;
                target = null;
                try
                {
                    tmp.semaphore.Release();
                }
                catch (Exception) { } // 直接忽略异常。
            }
        }
    }
}
