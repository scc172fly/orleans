using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs.StatisticMonitors
{
    /// <summary>
    /// EventHubStreamProvider 使用的对象池的默认监视器。
    /// </summary>
    public class DefaultEventHubBlockPoolMonitor : DefaultBlockPoolMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubBlockPoolMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">Event Hub 块池指标维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        public DefaultEventHubBlockPoolMonitor(EventHubBlockPoolMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("ObjectPoolId", dimensions.BlockPoolId) }, instruments)
        {
        }
    }
}
