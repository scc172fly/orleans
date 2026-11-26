using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Threading;

namespace Orleans.Timers.Internal
{
    /// <summary>
    /// 提供管理单发计时器的功能。
    /// </summary>
    public interface ITimerManager
    {
        /// <summary>
        /// 返回一个任务,该任务将在指定的时间跨度折叠或提供的取消符被取消时完成。
        /// </summary>
        /// <param name="timeSpan">时间间隔。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><see langword="true"/> ,如果计时器运行到完成;否则 <see langword="false"/>。</returns>
        Task<bool> Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default);
    }

    internal class TimerManagerImpl : ITimerManager
    {
        public Task<bool> Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default) => TimerManager.Delay(timeSpan, cancellationToken);
    }

    internal static class TimerManager
    {
        public static Task<bool> Delay(TimeSpan timeSpan, CancellationToken cancellationToken = default) => DelayUntil(DateTime.UtcNow + timeSpan, cancellationToken);

        public static Task<bool> DelayUntil(DateTime dueTime, CancellationToken cancellationToken = default)
        {
            var result = new DelayTimer(dueTime, cancellationToken);
            TimerManager<DelayTimer>.Register(result);
            return result.Completion;
        }

        private sealed class DelayTimer : ITimerCallback, ILinkedListElement<DelayTimer>
        {
            private readonly TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public DelayTimer(DateTime dueTime, CancellationToken cancellationToken)
            {
                this.DueTime = dueTime;
                this.CancellationToken = cancellationToken;
            }

            public Task<bool> Completion => this.completion.Task;

            public DateTime DueTime { get; }

            public CancellationToken CancellationToken { get; }

            public void OnTimeout() => this.completion.TrySetResult(true);

            public void OnCanceled() => this.completion.TrySetResult(false);

            DelayTimer? ILinkedListElement<DelayTimer>.Next { get; set; }
        }
    }

    /// <summary>
    /// 管理指定类型的定时器,在过期后将其解雇。
    /// </summary>
    /// <typeparam name="T">定时器类型。</typeparam>
    internal static class TimerManager<T> where T : class, ITimerCallback, ILinkedListElement<T>
    {
        /// <summary>
        /// 要求服务之前,排队最多可拒绝服务的次数。
        /// </summary>
        private const int MAX_STARVATION = 2;

        /// <summary>
        /// 定时器服务计数之间 的毫秒数 。
        /// </summary>
        private const int TIMER_TICK_MILLISECONDS = 50;

        /// <summary>
        /// 锁定保护 <see cref="allQueues"/>。
        /// </summary>
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object AllQueuesLock = new object();

#pragma warning disable IDE0052 // Remove unread private members
        private static readonly Timer QueueChecker;
#pragma warning restore IDE0052 // Remove unread private members

        /// <summary>
        /// 收集所有本地的线性定时器队列 。
        /// </summary>
        private static ThreadLocalQueue?[] allQueues = new ThreadLocalQueue[16];

        /// <summary>
        /// 当前线索的队列 。
        /// </summary>
        [ThreadStatic]
        private static ThreadLocalQueue? threadLocalQueue;

        static TimerManager()
        {
            var timerPeriod = TimeSpan.FromMilliseconds(TIMER_TICK_MILLISECONDS);
            QueueChecker = NonCapturingTimer.Create(_ => CheckQueues(), null, timerPeriod, timerPeriod);
        }

        /// <summary>
        /// 登记计时器。
        /// </summary>
        public static void Register(T timer)
        {
            ExpiredTimers? expired = null;
            var queue = EnsureCurrentThreadHasQueue();

            try
            {
                queue.Lock.Get();
                queue.AddTail(timer);

                if (queue.StarvationCount >= MAX_STARVATION)
                {
                    // 如果队列太饿,请马上接听
                    expired = new ExpiredTimers();
                    CheckQueueInLock(queue, DateTime.UtcNow, expired);
                    Interlocked.Exchange(ref queue.StarvationCount, 0);
                }
            }
            finally
            {
                queue.Lock.TryRelease();

                // 消防车在门外的定时器失效了
                expired?.FireTimers();
            }
        }

        private static void CheckQueues()
        {
            var expired = new ExpiredTimers();
            var now = DateTime.UtcNow;
            try
            {
                foreach (var queue in allQueues)
                {
                    if (queue == null)
                    {
                        continue;
                    }

                    if (!queue.Lock.TryGet())
                    {
                        // 检查是否饿死
                        if (Interlocked.Increment(ref queue.StarvationCount) > MAX_STARVATION)
                        {
                            // 如果队列饿死, 封锁直到锁可以获取。
                            queue.Lock.Get();
                            Interlocked.Exchange(ref queue.StarvationCount, 0);
                        }
                        else
                        {
                            // 继续到下一个队列 。
                            continue;
                        }
                    }

                    try
                    {
                        CheckQueueInLock(queue, now, expired);
                    }
                    finally
                    {
                        queue.Lock.TryRelease();
                    }
                }
            }
            finally
            {
                // 循环内外的超时计时器
                expired.FireTimers();
            }
        }

        private static void CheckQueueInLock(ThreadLocalQueue queue, DateTime now, ExpiredTimers expired)
        {
            T? previous = default;

            for (var current = queue.Head; current != null; current = current.Next)
            {
                if (current.CancellationToken.IsCancellationRequested || current.DueTime < now)
                {
                    // 取消排队, 添加到过期列表中, 以便稍后执行 。
                    queue.Remove(previous, current);
                    expired.AddTail(current);
                }
                else
                {
                    // 如果当前项目没有被删除, 请更新上一个 。
                    previous = current;
                }
            }
        }

        /// <summary>
        /// 返回当前线索的队列,如果还没有,则创建并注册一个。
        /// </summary>
        /// <returns>当前线串的 <see cref="ThreadLocalQueue"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ThreadLocalQueue EnsureCurrentThreadHasQueue()
        {
            return threadLocalQueue ?? (threadLocalQueue = InitializeThreadLocalQueue());

            ThreadLocalQueue InitializeThreadLocalQueue()
            {
                var threadLocal = new ThreadLocalQueue();
                while (true)
                {
                    lock (AllQueuesLock)
                    {
                        var queues = Volatile.Read(ref allQueues);

                        // 在现有的数组中找到一个位置来登记此线索 。
                        for (var i = 0; i < queues.Length; i++)
                        {
                            if (Volatile.Read(ref queues[i]) == null)
                            {
                                Volatile.Write(ref queues[i], threadLocal);
                                return threadLocal;
                            }
                        }

                        // 现有数组是满的, 所以将所有值复制到一个新的更大的数组中, 然后注册此线索 。
                        var newQueues = new ThreadLocalQueue[queues.Length * 2];
                        Array.Copy(queues, newQueues, queues.Length);
                        newQueues[queues.Length] = threadLocal;
                        Volatile.Write(ref allQueues, newQueues);
                        return threadLocal;
                    }
                }
            }
        }

        /// <summary>
        /// 持有每分钟的计时器数据。
        /// </summary>
        private sealed class ThreadLocalQueue : ILinkedList<T>
        {
            public readonly RecursiveInterlockedExchangeLock Lock = new RecursiveInterlockedExchangeLock();

            /// <summary>
            /// 上次服务以来, 队列中饥饿的次数 。
            /// </summary>
            public int StarvationCount;

            public T? Head { get; set; }

            public T? Tail { get; set; }
        }

        /// <summary>
        /// 持有已过期且应被解雇的计时器。
        /// </summary>
        private sealed class ExpiredTimers : ILinkedList<T>
        {
            public T? Head { get; set; }

            public T? Tail { get; set; }

            public void FireTimers()
            {
                var current = this.Head;
                while (current != null)
                {
                    try
                    {
                        if (current.CancellationToken.IsCancellationRequested)
                        {
                            current.OnCanceled();
                        }
                        else
                        {
                            current.OnTimeout();
                        }
                    }
                    catch
                    {
                        // 忽略射击中的任何例外。
                    }

                    current = current.Next;
                }
            }
        }
    }

    internal interface ITimerCallback
    {
        /// <summary>
        /// 时钟到期时UTC时间 。
        /// </summary>
        DateTime DueTime { get; }

        CancellationToken CancellationToken { get; }

        void OnTimeout();

        void OnCanceled();
    }

    /// <summary>
    /// 代表链接列表。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    internal interface ILinkedList<T> where T : ILinkedListElement<T>
    {
        /// <summary>
        /// 获取或设置列表的第一个元素 。
        /// 此值绝不能被用户代码访问或修改 。
        /// </summary>
        [MaybeNull, AllowNull]
        T Head { get; set; }

        /// <summary>
        /// 获取或设置列表中的最后元素。
        /// 此值绝不能被用户代码访问或修改 。
        /// </summary>
        [MaybeNull, AllowNull]
        T Tail { get; set; }
    }

    /// <summary>
    /// 表示链接列表中的一个元素。
    /// </summary>
    /// <typeparam name="TSelf">自定义类型。 执行此接口的类型 。</typeparam>
    internal interface ILinkedListElement<TSelf> where TSelf : ILinkedListElement<TSelf>
    {
        /// <summary>
        /// 列表中的下一个元素。
        /// 此值绝不能被用户代码访问或修改 。
        /// </summary>
        [MaybeNull, AllowNull]
        TSelf Next { get; set; }
    }

    internal static class LinkedList
    {
        /// <summary>
        /// 将项目附加到链接列表的尾端。
        /// </summary>
        /// <param name="list">链接列表。</param>
        /// <param name="element">附加的元素。</param>
        public static void AddTail<TList, TElement>(this TList list, TElement element)
            where TList : class, ILinkedList<TElement> where TElement : class, ILinkedListElement<TElement>
        {
            // 如果这是第一个元素, 请更新头部 。
            if (list.Head is null) list.Head = element;

            // 如果这不是第一个元素, 请更新当前尾巴 。
            var prevTail = list.Tail;
            if (!(prevTail is null)) prevTail.Next = element;

            // 更新尾巴
            list.Tail = element;
        }

        /// <summary>
        /// 从链接列表中删除项目。
        /// </summary>
        /// <param name="list">链接列表。</param>
        /// <param name="previous">前面的元素 <paramref name="current"/>.</param>
        /// <param name="current">要删除的元素。</param>
        public static void Remove<TList, TElement>(this TList list, TElement? previous, TElement current)
            where TList : class, ILinkedList<TElement> where TElement : class, ILinkedListElement<TElement>
        {
            var next = current.Next;

            // 如果删除第一个元素,请在下一个元素中点上前一个元素。
            if (!(previous is null)) previous.Next = next;

            // 如果删除第一个元素,请将尾巴指向下一个元素。
            if (ReferenceEquals(list.Head, current))
            {
                list.Head = next ?? previous;
            }

            // 如果删除最后一个元素,请将尾巴指向上一个元素。
            if (ReferenceEquals(list.Tail, current))
            {
                list.Tail = previous;
            }
        }
    }
}
