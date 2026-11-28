using Orleans.Providers.Streams.Common;
using System;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 缓存压力监视器记录对缓存的压力贡献值，并根据其
    /// 背压算法判断缓存是否处于压力状态。
    /// </summary>
    public interface ICachePressureMonitor
    {
        /// <summary>
        /// 向监视器记录缓存压力贡献值。
        /// </summary>
        /// <param name="cachePressureContribution"></param>
        void RecordCachePressureContribution(double cachePressureContribution);

        /// <summary>
        /// 判断监视器是否处于压力状态。
        /// </summary>
        /// <param name="utcNow"></param>
        /// <returns></returns>
        bool IsUnderPressure(DateTime utcNow);

        /// <summary>
        /// 用于报告缓存相关指标的缓存监视器。
        /// </summary>
        ICacheMonitor? CacheMonitor { set; }
    }
}
