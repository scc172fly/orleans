using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs.StatisticMonitors
{
    /// <summary>
    /// 偶微流流提供方生态系统默认缓存监视器
    /// </summary>
    public class DefaultEventHubCacheMonitor : DefaultCacheMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubCacheMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">事件枢纽缓存量度。</param>
        /// <param name="instruments">下 Orleans 运行工具。</param>
        public DefaultEventHubCacheMonitor(EventHubCacheMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("Partition", dimensions.EventHubPartition) }, instruments)
        {
        }
    }
}
