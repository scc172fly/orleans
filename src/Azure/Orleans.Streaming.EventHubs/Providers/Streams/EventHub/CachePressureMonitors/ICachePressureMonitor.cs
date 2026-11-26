using Orleans.Providers.Streams.Common;
using System;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 缓存压力监测器记录暗藏处的档案压力,确定该藏匿处是否因其 
    /// 反向压力算法
    /// </summary>
    public interface ICachePressureMonitor
    {
        /// <summary>
        /// 记录对监视器的缓存压力贡献
        /// </summary>
        /// <param name="cachePressureContribution"></param>
        void RecordCachePressureContribution(double cachePressureContribution);

        /// <summary>
        /// 确定显示器是否受到压力
        /// </summary>
        /// <param name="utcNow"></param>
        /// <returns></returns>
        bool IsUnderPressure(DateTime utcNow);

        /// <summary>
        /// 用于报告缓存相关度量的缓存监测器
        /// </summary>
        ICacheMonitor? CacheMonitor { set; }
    }
}
