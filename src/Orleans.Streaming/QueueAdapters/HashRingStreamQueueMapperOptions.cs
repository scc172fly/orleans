using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="HashRingBasedStreamQueueMapper"/>选项。
    /// </summary>
    public class HashRingStreamQueueMapperOptions
    {
        /// <summary>
        /// 获取或设置队列总数 。
        /// </summary>
        /// <value>队列总数。</value>
        public int TotalQueueCount { get; set; } = DEFAULT_NUM_QUEUES;

        /// <summary>
        /// 默认的数队列, 应该是两个的功率 。
        /// </summary>
        public const int DEFAULT_NUM_QUEUES = 8;
    }
}
