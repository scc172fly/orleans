using System;

namespace Orleans.Statistics
{

    /// <summary>
    /// 获取与托管环境有关的统计数据的功能。
    /// </summary>
    [Obsolete($"This functionality will be removed, use {nameof(IEnvironmentStatisticsProvider)}.{nameof(IEnvironmentStatisticsProvider.GetEnvironmentStatistics)} instead.")]
    public interface IHostEnvironmentStatistics
    {
        /// <summary>
        /// 以字节表示主机的全部物理内存。
        /// </summary>
        /// <example>
        /// 16 GB 对应 <c>16426476000L</c>。
        /// </example>
        long? TotalPhysicalMemory { get; }

        /// <summary>
        /// 将主机 CPU 使用量从 0.0- 1000. 中获取。
        /// </summary>
        /// <example>
        /// <c>70.0f</c> 70% CPU使用率的 CPU 。
        /// </example>
        float? CpuUsage { get; }

        /// <summary>
        /// 以字节表示主机可分配的全部内存。
        /// </summary>
        /// <example>
        /// 14 GB 对应 <c>14426476000L</c>。
        /// </example>
        long? AvailableMemory { get; }
    }
}
