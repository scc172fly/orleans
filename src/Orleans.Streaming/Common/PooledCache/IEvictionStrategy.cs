using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// PooledQueueCache驱逐战略
    /// </summary>
    public interface IEvictionStrategy
    {
        /// <summary>
        /// 获取 <see cref="IPurgeObservable"/>,由缓存执行,以采取与清除有关的行动,并被驱逐战略所援引。
        /// </summary>
        IPurgeObservable PurgeObservable { set; }

        /// <summary>
        /// 获取或设置清洗完成后要使用的方法 。
        /// </summary>
        Action<CachedMessage?, CachedMessage?>? OnPurged { get; set; }

        /// <summary>
        /// 当拉动代理器试图对缓存进行清除时应该使用的方法
        /// </summary>
        /// <param name="utcNow">当前时间( UTC)</param>
        void PerformPurge(DateTime utcNow);

        /// <summary>
        /// 当数据适配器分配新块时应该调用的方法
        /// </summary>
        /// <param name="newBlock">新块。</param>
        void OnBlockAllocated(FixedSizeBuffer newBlock);
    }

    /// <summary>
    /// 与清除行动有关的功能。
    /// </summary>
    public interface IPurgeObservable
    {
        /// <summary>
        /// 删除缓存中最老的信件 。
        /// </summary>
        void RemoveOldestMessage();

        /// <summary>
        /// 获取缓存中的最新消息 。
        /// </summary>
        CachedMessage? Newest { get; }

        /// <summary>
        /// 获取缓存中最古老的信息 。
        /// </summary>
        CachedMessage? Oldest { get; }

        /// <summary>
        /// 获得信息计数。
        /// </summary>
        int ItemCount { get; }

        /// <summary>
        /// 获得一个值,以显示此实例是否为空。
        /// </summary>
        bool IsEmpty { get; }
    }
}
