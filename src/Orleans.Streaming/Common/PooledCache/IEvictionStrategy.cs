using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// PooledQueueCache 的逐出策略。
    /// </summary>
    public interface IEvictionStrategy
    {
        /// <summary>
        /// 获取 <see cref="IPurgeObservable"/>；它由缓存实现以执行清除相关操作，并由逐出策略调用。
        /// </summary>
        IPurgeObservable PurgeObservable { set; }

        /// <summary>
        /// 获取或设置清除完成后要调用的方法。
        /// </summary>
        Action<CachedMessage?, CachedMessage?>? OnPurged { get; set; }

        /// <summary>
        /// 当拉取代理尝试清除缓存时应调用的方法。
        /// </summary>
        /// <param name="utcNow">当前时间（UTC）。</param>
        void PerformPurge(DateTime utcNow);

        /// <summary>
        /// 数据适配器分配新块时应调用的方法。
        /// </summary>
        /// <param name="newBlock">新块。</param>
        void OnBlockAllocated(FixedSizeBuffer newBlock);
    }

    /// <summary>
    /// 提供清除相关操作的功能。
    /// </summary>
    public interface IPurgeObservable
    {
        /// <summary>
        /// 移除缓存中最早的消息。
        /// </summary>
        void RemoveOldestMessage();

        /// <summary>
        /// 获取缓存中最新的消息。
        /// </summary>
        CachedMessage? Newest { get; }

        /// <summary>
        /// 获取缓存中最早的消息。
        /// </summary>
        CachedMessage? Oldest { get; }

        /// <summary>
        /// 获取消息数量。
        /// </summary>
        int ItemCount { get; }

        /// <summary>
        /// 获取一个值，指示此实例是否为空。
        /// </summary>
        bool IsEmpty { get; }
    }
}
