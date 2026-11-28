using System;

namespace Orleans.Statistics
{
    /// <summary>
    /// 提供访问应用程序环境相关统计信息的功能。
    /// </summary>
    [Obsolete($"This functionality will be removed, use {nameof(IEnvironmentStatisticsProvider)}.{nameof(IEnvironmentStatisticsProvider.GetEnvironmentStatistics)} instead.")]
    public interface IAppEnvironmentStatistics
    {
        /// <summary>
        /// 如果可获取，返回内存总用量（以字节为单位）。
        /// </summary>
        long? MemoryUsage { get; }
    }
}
