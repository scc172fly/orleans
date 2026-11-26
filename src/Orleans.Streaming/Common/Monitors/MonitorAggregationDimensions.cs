namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 接收器监测器的聚合尺寸。
    /// </summary>
    public class ReceiverMonitorDimensions
    {
        /// <summary>
        /// 初始化 <see cref="ReceiverMonitorDimensions"/> 类的新实例。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        public ReceiverMonitorDimensions(string queueId)
        {
            this.QueueId = queueId;
        }

        /// <summary>
        /// 初始化 <see cref="ReceiverMonitorDimensions"/> 类的新实例。
        /// </summary>
        public ReceiverMonitorDimensions()
        {
        }

        /// <summary>
        /// 获取队列标识符。
        /// </summary>
        public string QueueId { get; set; } = null!; // 由构建器或对象初始化器设置 。
    }

    /// <summary>
    /// 缓存监视器的聚合维度
    /// </summary>
    public class CacheMonitorDimensions : ReceiverMonitorDimensions
    {
        /// <summary>
        /// 初始化 <see cref="CacheMonitorDimensions"/> 类的新实例。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <param name="blockPoolId">块集合识别符号。</param>
        public CacheMonitorDimensions(string queueId, string blockPoolId)
            : base(queueId)
        {
            this.BlockPoolId = blockPoolId;
        }

        /// <summary>
        /// 获取或设置街区池识别码。
        /// </summary>
        /// <value>块集合识别符号。</value>
        public string BlockPoolId { get; set; }
    }

    /// <summary>
    /// 区块池监测器的聚合尺寸。
    /// </summary>
    public class BlockPoolMonitorDimensions
    {
        /// <summary>
        /// 初始化 <see cref="BlockPoolMonitorDimensions"/> 类的新实例。
        /// </summary>
        /// <param name="blockPoolId">块集合识别符号。</param>
        public BlockPoolMonitorDimensions(string blockPoolId)
        {
            this.BlockPoolId = blockPoolId;
        }

        /// <summary>
        /// 获取或设置街区池识别码。
        /// </summary>
        /// <value>块集合识别符号。</value>
        public string BlockPoolId { get; set; }
    }
}
