using System;

namespace Orleans.Statistics
{

    /// <summary>
    /// 提供访问宿主环境相关统计信息的功能。
    /// </summary>
    [Obsolete($"This functionality will be removed, use {nameof(IEnvironmentStatisticsProvider)}.{nameof(IEnvironmentStatisticsProvider.GetEnvironmentStatistics)} instead.")]
    public interface IHostEnvironmentStatistics
    {
        /// <summary>
        /// 获取宿主机的物理内存总量（以字节为单位）。
        /// </summary>
        /// <example>
        /// <c>16426476000L</c> 表示 16 GB。
        /// </example>
        long? TotalPhysicalMemory { get; }

        /// <summary>
        /// 获取宿主机 CPU 使用率，范围为 0.0 至 100.0。
        /// </summary>
        /// <example>
        /// <c>70.0f</c> 表示 CPU 使用率为 70%。
        /// </example>
        float? CpuUsage { get; }

        /// <summary>
        /// 获取宿主机上可供分配的内存总量（以字节为单位）。
        /// </summary>
        /// <example>
        /// <c>14426476000L</c> 表示 14 GB。
        /// </example>
        long? AvailableMemory { get; }
    }
}
