using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 一种相互排斥的结合机制,通过“使用”支持范围界定。
    /// </summary>
    /// <remarks>
    /// (编自 http://blogs.msdn.com/b/pfxteam/archive/2012/02/12/10266988.aspx)
    ///
    /// 与 <b>亚同步</b>, 和 <b>锁定锁定</b> 关键字有问题:
    /// <list type="bullet">
    ///     <item><description><b>锁</b> 将让线索在等待对代码关键部分的独家访问时被阻塞。</description></item>
    ///     <item><description>下 <b>等待等待</b> 关键字不能在 <b>锁定锁定</b> 构造。</description></item>
    /// </list>
    ///
    /// 有时提供对代码关键部分的独家访问仍然有用。 AsyncLock 提供语义
    /// 与(非递录性)哑音相对应,同时符合同步方案拟订原则。
    /// </remarks>
    /// <example>
    /// 以下例子执行一些需要锁在门下完成的工作:
    /// <code>
    /// class Test
    /// {
    ///     private AsyncLock _initLock = new AsyncLock();
    ///     public async Task&lt;int> WorkUnderLock()
    ///     {
    ///             using (await _initLock.LockAsync()) // 类似于 lock(_initLock)
    ///             {
    ///                  return await DoSomeWork();
    ///             }
    ///     }
    /// }
    /// </code>
    /// </example>
    ///
    /// 我们决定尽量保持执行的简单性,尽可能模仿普通哑音的语义。
    /// 1) AsyncLock 不可工作,因为我们不想让开发者选择错误手动处理锁定
    /// 可能有些未释放 LockReleasers。
    /// 2) AsyncLock 不必执行最后处理功能。 SemaphoreSlim 的基本资源最终将由 .NET释放,
    /// 何时 SemaphoreSlim 已完成。 AsyncLock 无法加速
    /// 3) LockReleaser 是无法执行“使用”模式的。
    /// LockReleaser 不需要执行最后处理功能。如果用户忘记丢弃 LockReleaser (与忘记释放哑巴相似)
    /// AsyncLock 将仍然锁定, 这可能会导致僵局。 这是可以的, 因为这些都是非常普通的哑语语义学 - 如果忘记解锁哑语的话, 它会一直被锁住 。
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
        /// 迅速获得锁
        /// </summary>
        /// <returns>解锁必须使用的 <see cref="IDisposable"/> 。</returns>
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

                // 首先是取消,下个释放, 所以即使发布扔了, 我们不再持有参考文献。
                AsyncLock tmp = target;
                target = null;
                try
                {
                    tmp.semaphore.Release();
                }
                catch (Exception) { } // 忽略例外
            }
        }
    }
}
