
namespace Orleans.Configuration
{
    /// <summary>
    /// EHGeneratorStreamProvider设置类
    /// </summary>
    public class EventDataGeneratorStreamOptions
    {
        /// <summary>
        /// 配置 Eventhub 分区计数 。 EventDataGeneratorStreamProvider 在初始化时, 将生成基于此计数的相同组分隔 。
        /// 例如,如果分区计数为5时,则产生的分区将是分区-0、分区1、分区2、分区3、分区3、分区4
        /// </summary>
        public int EventHubPartitionCount = DefaultEventHubPartitionCount;
        /// <summary>
        /// 默认 EventHubPartitionRangeStart
        /// </summary>
        public const int DefaultEventHubPartitionCount = 4;
    }
}
