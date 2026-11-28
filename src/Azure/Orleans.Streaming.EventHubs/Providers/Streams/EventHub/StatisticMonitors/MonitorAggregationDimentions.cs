
namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 监控聚合维度的基类，用于保存监控目标的相关信息。
    /// 监视器可使用这些信息构建自己的聚合维度。
    /// </summary>
    public class EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// Event Hub 路径。
        /// </summary>
        public string EventHubPath { get; set; } = null!;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="ehHubPath"></param>
        public EventHubMonitorAggregationDimensions(string ehHubPath)
        {
            this.EventHubPath = ehHubPath;
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="dimensions"></param>
        public EventHubMonitorAggregationDimensions(EventHubMonitorAggregationDimensions dimensions)
        {
            this.EventHubPath = dimensions.EventHubPath;
        }

        /// <summary>
        /// 无参构造函数。
        /// </summary>
        public EventHubMonitorAggregationDimensions()
        {
        }
    }

    /// <summary>
    /// EventHubReceiverMonitor 的聚合维度。
    /// </summary>
    public class EventHubReceiverMonitorDimensions : EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// Event Hub 分区。
        /// </summary>
        public string EventHubPartition { get; set; } = null!;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="ehPartition"></param>
        public EventHubReceiverMonitorDimensions(EventHubMonitorAggregationDimensions dimensions, string ehPartition)
            : base(dimensions)
        {
            this.EventHubPartition = ehPartition;
        }

        /// <summary>
        /// 无参构造函数。
        /// </summary>
        public EventHubReceiverMonitorDimensions()
        {
        }
    }

    /// <summary>
    /// Event Hub 流提供程序体系中缓存监视器的聚合维度。
    /// </summary>
    public class EventHubCacheMonitorDimensions : EventHubReceiverMonitorDimensions
    {
        /// <summary>
        /// 此缓存所属的块池。
        /// </summary>
        public string BlockPoolId { get; set; } = null!;

        /// <summary>
        /// 构造函数。
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
        /// 无参构造函数。
        /// </summary>
        public EventHubCacheMonitorDimensions()
        {
        }
    }

    /// <summary>
    /// Event Hub 流提供程序体系中块池监视器的聚合维度。
    /// </summary>
    public class EventHubBlockPoolMonitorDimensions : EventHubMonitorAggregationDimensions
    {
        /// <summary>
        /// 块池 ID。
        /// </summary>
        public string BlockPoolId { get; set; } = null!;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="blockPoolId"></param>
        public EventHubBlockPoolMonitorDimensions(EventHubMonitorAggregationDimensions dimensions, string blockPoolId)
            : base(dimensions)
        {
            this.BlockPoolId = blockPoolId;
        }

        /// <summary>
        /// 无参构造函数。
        /// </summary>
        public EventHubBlockPoolMonitorDimensions()
        {
        }
    }
}
