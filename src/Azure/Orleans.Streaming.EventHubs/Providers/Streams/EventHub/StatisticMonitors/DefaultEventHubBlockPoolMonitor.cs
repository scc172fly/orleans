using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs.StatisticMonitors
{
    /// <summary>
    /// EventHubStreamProvider所用对象群的默认监视器
    /// </summary>
    public class DefaultEventHubBlockPoolMonitor : DefaultBlockPoolMonitor
    {
        /// <summary>
        /// 初始化 <see cref="DefaultEventHubBlockPoolMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">活动中心组合组合的衡量尺度。</param>
        /// <param name="instruments">下 Orleans 运行工具。</param>
        public DefaultEventHubBlockPoolMonitor(EventHubBlockPoolMonitorDimensions dimensions, OrleansInstruments instruments)
            : base(new KeyValuePair<string, object>[] { new("Path", dimensions.EventHubPath), new("ObjectPoolId", dimensions.BlockPoolId) }, instruments)
        {
        }
    }
}
