using System;

namespace Orleans.Statistics
{
    /// <summary>
    /// 提供获取应用环境统计数据的功能。
    /// </summary>
    [Obsolete($"This functionality will be removed, use {nameof(IEnvironmentStatisticsProvider)}.{nameof(IEnvironmentStatisticsProvider.GetEnvironmentStatistics)} instead.")]
    public interface IAppEnvironmentStatistics
    {
        /// <summary>
        /// 如果可用,则获取总内存使用量,以字节表示。
        /// </summary>
        long? MemoryUsage { get; }
    }
}
