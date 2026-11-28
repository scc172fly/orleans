using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 池化队列缓存将数据保存在紧凑结构中，需要快速转换为各种
    /// 其他格式。由于数据格式可能随队列类型和数据格式变化，
    /// 此接口允许适配器开发者构建适合
    /// 各类队列数据的自定义数据转换逻辑。
    /// </summary>
    public interface ICacheDataAdapter
    {
        /// <summary>
        /// 将缓存消息转换为用于投递的批次容器。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <returns>批次容器。</returns>
        IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage);

        /// <summary>
        /// 从缓存消息中获取流序列令牌。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <returns>序列令牌。</returns>
        StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage);

        /// <summary>
        /// 比较缓存消息与流序列令牌。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <param name="token">序列令牌。</param>
        /// <returns>指示缓存消息与令牌相对顺序的值。</returns>
        /// <remarks>
        /// 默认实现使用无需分配内存的序列号和事件索引字段进行比较。
        /// 使用外部偏移量的提供程序可以重写此方法，直接比较编码后的偏移量数据。
        /// </remarks>
        int Compare(ref CachedMessage cachedMessage, StreamSequenceToken token)
            => cachedMessage.Compare(token);
    }
}
