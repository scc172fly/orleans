
namespace Orleans.Configuration
{
    /// <summary>
    /// EHGeneratorStreamProvider 的设置类。
    /// </summary>
    public class EventDataGeneratorStreamOptions
    {
        /// <summary>
        /// 配置所需 Event Hub 分区数量。初始化时，EventDataGeneratorStreamProvider 根据数量生成同一组分区。
        /// 例如，分区数量设为 5 时，生成的分区为 partition-0、partition-1、partition-2、partition-3、partition-4。
        /// </summary>
        public int EventHubPartitionCount = DefaultEventHubPartitionCount;
        /// <summary>
        /// 默认 EventHubPartitionRangeStart。
        /// </summary>
        public const int DefaultEventHubPartitionCount = 4;
    }
}
