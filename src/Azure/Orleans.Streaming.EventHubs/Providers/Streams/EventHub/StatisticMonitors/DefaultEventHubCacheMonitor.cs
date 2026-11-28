using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs.StatisticMonitors
{
    /// <summary>
    /// Event Hub 流提供程序体系的默认缓存监视器。
    /// </summary>
    public class DefaultEventHubCacheMonitor : DefaultCacheMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubCacheMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">Event Hub 缓存指标维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        public DefaultEventHubCacheMonitor(EventHubCacheMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("Partition", dimensions.EventHubPartition) }, instruments)
        {
        }
    }
}
