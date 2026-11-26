namespace Orleans.Streams
{
    /// <summary>
    /// 为指定队列创建 <see cref="IQueueCache"/> 功能。
    /// </summary>
    public interface IQueueAdapterCache
    {
        /// <summary>
        /// 为指定的队列代号创建缓存
        /// </summary>
        /// <param name="queueId">队列代号。</param>
        /// <returns>队列缓存。</returns>
        IQueueCache CreateQueueCache(QueueId queueId);
    }
}
