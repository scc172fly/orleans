
namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 监测聚合尺寸的基础级,这是监测目标的信息袋。 
    /// 监测器可以利用这个信息袋构建其聚合尺寸。
    /// </summary>
    public class EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// 平均路径
        /// </summary>
        public string EventHubPath { get; set; } = null!;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="ehHubPath"></param>
        public EventHubMonitorAggregationDimensions(string ehHubPath)
        {
            this.EventHubPath = ehHubPath;
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="dimensions"></param>
        public EventHubMonitorAggregationDimensions(EventHubMonitorAggregationDimensions dimensions)
        {
            this.EventHubPath = dimensions.EventHubPath;
        }

        /// <summary>
        /// 零参数构建器
        /// </summary>
        public EventHubMonitorAggregationDimensions()
        {
        }
    }

    /// <summary>
    /// EventHubReceiverMonitor的聚合尺寸
    /// </summary>
    public class EventHubReceiverMonitorDimensions : EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// 偶形分区
        /// </summary>
        public string EventHubPartition { get; set; } = null!;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="ehPartition"></param>
        public EventHubReceiverMonitorDimensions(EventHubMonitorAggregationDimensions dimensions, string ehPartition)
            : base(dimensions)
        {
            this.EventHubPartition = ehPartition;
        }

        /// <summary>
        /// 零参数构建器
        /// </summary>
        public EventHubReceiverMonitorDimensions()
        {
        }
    }

    /// <summary>
    /// Eventhub河提供系统生态系统使用的缓存监测器的聚合尺寸
    /// </summary>
    public class EventHubCacheMonitorDimensions : EventHubReceiverMonitorDimensions
    {
        /// <summary>
        /// 此缓存属于的块块集合
        /// </summary>
        public string BlockPoolId { get; set; } = null!;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="ehPartition"></param>
        /// <param name="blockPoolId"></param>
        public EventHubCacheMonitorDimensions(EventHubMonitorAggregationDimensions dimensions, string ehPartition, string blockPoolId)
            : base(dimensions, ehPartition)
        {
            this.BlockPoolId = blockPoolId;
        }

        /// <summary>
        /// 零参数构建器
        /// </summary>
        public EventHubCacheMonitorDimensions()
        {
        }
    }

    /// <summary>
    /// Eventhub 流提供方生态系统使用的区块池监测器的聚合尺寸
    /// </summary>
    public class EventHubBlockPoolMonitorDimensions : EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// 块池
        /// </summary>
        public string BlockPoolId { get; set; } = null!;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="blockPoolId"></param>
        public EventHubBlockPoolMonitorDimensions(EventHubMonitorAggregationDimensions dimensions, string blockPoolId)
            : base(dimensions)
        {
            this.BlockPoolId = blockPoolId;
        }

        /// <summary>
        /// 零参数构建器
        /// </summary>
        public EventHubBlockPoolMonitorDimensions()
        {
        }
    }
}
