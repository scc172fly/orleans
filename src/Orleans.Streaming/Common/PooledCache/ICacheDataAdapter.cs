using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 需要转换成各种结构的紧密包装结构中集合队列缓存数据
    ///   由于数据格式可能因队列类型和数据格式而变化,
    ///   此界面允许调试器开发者建立适合 
    ///   各种队列数据类型。
    /// </summary>
    public interface ICacheDataAdapter
    {
        /// <summary>
        /// 将缓存信件转换成批量容器交付
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <returns>货箱</returns>
        IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage);

        /// <summary>
        /// 从缓存信件获取流序序列符号 。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <returns>序列符号。</returns>
        StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage);

        /// <summary>
        /// 将缓存信件与串流序列符号比较 。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <param name="token">序列符号。</param>
        /// <returns>表示缓存信件和牌子的相对顺序的值。</returns>
        /// <remarks>
        /// 默认实施使用不分配序列编号和事件索引字段。
        /// 提供方如果有外部抵消,可以取代这种方法,直接比较编码的抵销数据。
        /// </remarks>
        int Compare(ref CachedMessage cachedMessage, StreamSequenceToken token)
            => cachedMessage.Compare(token);
    }
}
