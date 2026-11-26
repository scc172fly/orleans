using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 默认 EventHub 接收器监测器,使用日志 PKI 支持跟踪测量。
    /// </summary>
    public class DefaultEventHubReceiverMonitor : DefaultQueueAdapterReceiverMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubReceiverMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">事件枢纽接收器的量度尺寸。</param>
        /// <param name="instruments">下 Orleans 运行工具。</param>
        public DefaultEventHubReceiverMonitor(EventHubReceiverMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("Partition", dimensions.EventHubPartition) }, instruments)
        {
        }
    }

}
