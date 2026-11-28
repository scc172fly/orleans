using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Orleans
{
    /// <summary>
    /// 泛型 <see cref="Interner{K, T}"/> 类使用的常量。
    /// </summary>
    internal static class InternerConstants
    {
        /* 根据 ConcurrentDictionary 的扩容策略建议采用以下缓存大小
        // ConcurrentDictionary 的内部实现会将大小调整为质数（不能被 3、5 或 7 整除）
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
    /// 提供对驻留对象的弱引用缓存。
    /// Interner 用于优化垃圾回收。
    /// 它用于存储频繁分配且可能具有较长生命周期的对象。
    /// 这意味着这些对象可能迅速占满第 2 代堆，导致频繁且代价高昂的全堆垃圾回收。
    /// 具体而言，抵达 Silo 的消息及其所有标头和 ID 可能存活足够长的时间，晋升到第 2 代堆。
    /// 因此，将所有 ID 存入 Interner，以便在不同消息之间复用其内存。
    /// </summary>
    /// <typeparam name="TKey">用作驻留键的对象类型。</typeparam>
    /// <typeparam name="TValue">需要驻留的对象类型。</typeparam>
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
        /// <param name="initialSize">驻留映射的初始大小。</param>
        public Interner(int initialSize = InternerConstants.SIZE_SMALL)
        {
            int concurrencyLevel = Environment.ProcessorCount; // 这是 .NET Core 中 ConcurrentDictionary 在容量为 31 时采用的默认并发级别。
            if (initialSize >= InternerConstants.SIZE_MEDIUM) concurrencyLevel *= 4;
            if (initialSize >= InternerConstants.SIZE_LARGE) concurrencyLevel *= 4;
            concurrencyLevel = Math.Min(concurrencyLevel, 1024);
            this.internCache = new ConcurrentDictionary<TKey, WeakReference<TValue>>(concurrencyLevel, initialSize);

            var period = TimeSpan.FromMinutes(10);
            var dueTime = period + TimeSpan.FromTicks(Random.Shared.Next((int)TimeSpan.TicksPerMinute)); // 添加少量初始随机扰动。
            cacheCleanupTimer = new Timer(InternCacheCleanupTimerCallback, null, dueTime, period);
        }

        /// <summary>
        /// 查找指定键对应的缓存对象；若不存在，则调用提供的创建函数生成新对象。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="creatorFunc">如果不存在缓存副本，用于创建新对象并将其存储到此键的函数。</param>
        /// <returns>指定键对应的对象，可以是先前缓存的副本，也可以是新创建的对象。</returns>
        public TValue FindOrCreate(TKey key, Func<TKey, TValue> creatorFunc)
        {
            // 尝试从缓存获取现有值。
            // 如果缓存条目不存在，则使用创建函数生成并插入新值。
            if (!internCache.TryGetValue(key, out var cacheEntry))
            {
                var obj = creatorFunc(key);
                internCache[key] = new WeakReference<TValue>(obj);
                return obj;
            }

            // 如果缓存条目已存在，则判断其中的值是否仍然有效。
            if (!cacheEntry.TryGetTarget(out var result))
            {
                // 创建新对象并将其重新插入缓存，以确保该条目仍然有效。
                var obj = creatorFunc(key);
                cacheEntry.SetTarget(obj);
                return obj;
            }

            return result;
        }

        /// <summary>
        /// 查找指定键对应的缓存对象；若不存在，则调用提供的创建函数生成新对象。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="creatorFunc">如果不存在缓存副本，用于创建新对象并将其存储到此键的函数。</param>
        /// <param name="state">要传递给 <paramref name="creatorFunc"/> 的状态。</param>
        /// <returns>指定键对应的对象，可以是先前缓存的副本，也可以是新创建的对象。</returns>
        public TValue FindOrCreate<TState>(TKey key, Func<TKey, TState, TValue> creatorFunc, TState state)
        {
            // 尝试从缓存获取现有值。
            // 如果缓存条目不存在，则使用创建函数生成并插入新值。
            if (!internCache.TryGetValue(key, out var cacheEntry))
            {
                var obj = creatorFunc(key, state);
                internCache[key] = new WeakReference<TValue>(obj);
                return obj;
            }

            // 如果缓存条目已存在，则判断其中的值是否仍然有效。
            if (!cacheEntry.TryGetTarget(out var result))
            {
                // 创建新对象并将其重新插入缓存，以确保该条目仍然有效。
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
