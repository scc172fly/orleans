using System;
using System.Collections.Generic;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 诊断的接口。
    /// </summary>
    public interface ILogConsistencyDiagnostics
    {
        /// <summary>转而为这个对数一致的 grain收集统计数据。</summary>
        void EnableStatsCollection();

        /// <summary>关闭此对数一致的 grain的统计收集。</summary>
        void DisableStatsCollection();

        /// <summary>获取为此日志一致 grain而收集的统计数据。</summary>
        LogConsistencyStatistics? GetStats();

    }

    /// <summary>
    /// 使用日志一致性的 grains 统计数据集。见 <see cref="LogConsistentGrain{T}"/>
    /// </summary>
    public class LogConsistencyStatistics
    {
        /// <summary>
        /// 从事件名称到事件计数的地图
        /// </summary>
        public Dictionary<string, long> EventCounters = null!;
        /// <summary>
        /// 一份所有已测量的稳定延缓时间列表
        /// </summary>
        public List<int> StabilizationLatenciesInMsecs = null!;
    }
}
