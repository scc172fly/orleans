namespace Orleans.Streams
{
    /// <summary>
    /// 提供为指定队列创建 <see cref="IQueueCache"/> 的功能。
    /// </summary>
    public interface IQueueAdapterCache
    {
        /// <summary>
        /// 为指定队列 ID 创建缓存。
        /// </summary>
        /// <param name="queueId">队列 ID。</param>
        /// <returns>队列缓存。</returns>
        IQueueCache CreateQueueCache(QueueId queueId);
    }
}
