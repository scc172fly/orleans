using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 默认 Event Hub 接收器监视器，使用日志记录器的 PKI 支持来跟踪指标。
    /// </summary>
    public class DefaultEventHubReceiverMonitor : DefaultQueueAdapterReceiverMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubReceiverMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">Event Hub 接收器指标维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        public DefaultEventHubReceiverMonitor(EventHubReceiverMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("Partition", dimensions.EventHubPartition) }, instruments)
        {
        }
    }

}
