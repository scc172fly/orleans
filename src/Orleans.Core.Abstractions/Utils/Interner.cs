using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Orleans
{
    /// <summary>
    /// 通用 <see cref="Interner{K, T}"/> 类使用的常数。
    /// </summary>
    internal static class InternerConstants
    {
        /* 根据 ConcurrentDictionary扩大政策提出的建议缓冲量
        // 内部实施 ConcurrentDictionary 调整成质数(不可3或5或7分辨)
        31
        67
        137
        277
        557
        1,117
        2,237
        4,477
        8,957
        17,917
        35,837
        71,677
        143,357
        286,717
        573,437
        1,146,877
        2,293,757
        4,587,517
        9,175,037
        18,350,077
        36,700,157
        */
        public const int SIZE_SMALL = 67;
        public const int SIZE_MEDIUM = 1117;
        public const int SIZE_LARGE = 143357;
        public const int SIZE_X_LARGE = 2293757;
    }

    /// <summary>
    /// 提供参考不足的被扣押对象缓存。
    /// 内部人员被用来优化垃圾收集工作。
    /// 我们用它储存经常分配的物品,这些物品可能具有较长寿命。
    /// 这意味着这些物体可能很快填充原2,并导致经常花费昂贵的堆积物。
    /// 具体而言,到达 silo 和信头及信标内所有身份识别码的信息可能活得足够长,足以达到2等离子。
    /// 因此,我们把所有ID存储在内部 以便重新使用他们的记忆 跨越不同的讯息。
    /// </summary>
    /// <typeparam name="TKey">用于实习生钥匙的物体类型。</typeparam>
    /// <typeparam name="TValue">要被隔离的对象类型。</typeparam>
    internal sealed class Interner<TKey, TValue> : IDisposable
        where TKey : IEquatable<TKey>
        where TValue : class
    {
        private readonly Timer cacheCleanupTimer;

        [NonSerialized]
        private readonly ConcurrentDictionary<TKey, WeakReference<TValue>> internCache;

        /// <summary>
        /// 初始化 <see cref="Interner{K, T}"/> 类的新实例。
        /// </summary>
        /// <param name="initialSize">内部绘图的初始大小。</param>
        public Interner(int initialSize = InternerConstants.SIZE_SMALL)
        {
            int concurrencyLevel = Environment.ProcessorCount; // 默认默认 ConcurrentDictionary 类内 .NET 31号核心
            if (initialSize >= InternerConstants.SIZE_MEDIUM) concurrencyLevel *= 4;
            if (initialSize >= InternerConstants.SIZE_LARGE) concurrencyLevel *= 4;
            concurrencyLevel = Math.Min(concurrencyLevel, 1024);
            this.internCache = new ConcurrentDictionary<TKey, WeakReference<TValue>>(concurrencyLevel, initialSize);

            var period = TimeSpan.FromMinutes(10);
            var dueTime = period + TimeSpan.FromTicks(Random.Shared.Next((int)TimeSpan.TicksPerMinute)); // 添加一些初始音量
            cacheCleanupTimer = new Timer(InternCacheCleanupTimerCallback, null, dueTime, period);
        }

        /// <summary>
        /// 查找带有指定密钥的对象缓存副本, 否则使用所提供的创建功能创建新密钥 。
        /// </summary>
        /// <param name="key">查找密钥键</param>
        /// <param name="creatorFunc">函数,在没有缓存副本的情况下创建新对象并存储此密钥</param>
        /// <returns>对象,带有指定的密钥 - 之前的缓存副本或新创建的</returns>
        public TValue FindOrCreate(TKey key, Func<TKey, TValue> creatorFunc)
        {
            // 试图从缓存获取现有值 。
            // 如果没有缓存条目, 请使用创建功能创建并插入新的缓存条目 。
            if (!internCache.TryGetValue(key, out var cacheEntry))
            {
                var obj = creatorFunc(key);
                internCache[key] = new WeakReference<TValue>(obj);
                return obj;
            }

            // 如果存在缓存条目,请确定它是否仍然持有有效值。
            if (!cacheEntry.TryGetTarget(out var result))
            {
                // 创建新对象, 通过重新插入缓存来确保条目仍然有效 。
                var obj = creatorFunc(key);
                cacheEntry.SetTarget(obj);
                return obj;
            }

            return result;
        }

        /// <summary>
        /// 查找带有指定密钥的对象缓存副本, 否则使用所提供的创建功能创建新密钥 。
        /// </summary>
        /// <param name="key">查找密钥键</param>
        /// <param name="creatorFunc">函数,在没有缓存副本的情况下创建新对象并存储此密钥</param>
        /// <param name="state">国家将转至 <paramref name="creatorFunc"/>。</param>
        /// <returns>对象,带有指定的密钥 - 之前的缓存副本或新创建的</returns>
        public TValue FindOrCreate<TState>(TKey key, Func<TKey, TState, TValue> creatorFunc, TState state)
        {
            // 试图从缓存获取现有值 。
            // 如果没有缓存条目, 请使用创建功能创建并插入新的缓存条目 。
            if (!internCache.TryGetValue(key, out var cacheEntry))
            {
                var obj = creatorFunc(key, state);
                internCache[key] = new WeakReference<TValue>(obj);
                return obj;
            }

            // 如果存在缓存条目,请确定它是否仍然持有有效值。
            if (!cacheEntry.TryGetTarget(out var result))
            {
                // 创建新对象, 通过重新插入缓存来确保条目仍然有效 。
                var obj = creatorFunc(key, state);
                cacheEntry.SetTarget(obj);
                return obj;
            }

            return result;
        }

        private void InternCacheCleanupTimerCallback(object? state)
        {
            foreach (var e in internCache)
            {
                if (!e.Value.TryGetTarget(out _))
                {
                    internCache.TryRemove(e.Key, out _);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            cacheCleanupTimer?.Dispose();
        }
    }
}
