using System;
using System.Collections.Generic;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 诊断接口。
    /// </summary>
    public interface ILogConsistencyDiagnostics
    {
        /// <summary>启用此日志一致性 Grain 的统计数据收集。</summary>
        void EnableStatsCollection();

        /// <summary>禁用此日志一致性 Grain 的统计数据收集。</summary>
        void DisableStatsCollection();

        /// <summary>获取此日志一致性 Grain 已收集的统计数据。</summary>
        LogConsistencyStatistics? GetStats();

    }

    /// <summary>
    /// 使用日志一致性的 Grain 的统计数据集合。请参阅 <see cref="LogConsistentGrain{T}"/>。
    /// </summary>
    public class LogConsistencyStatistics
    {
        /// <summary>
        /// 从事件名称到事件计数的映射。
        /// </summary>
        public Dictionary<string, long> EventCounters = null!;
        /// <summary>
        /// 所有已测量的稳定延迟的列表。
        /// </summary>
        public List<int> StabilizationLatenciesInMsecs = null!;
    }
}
