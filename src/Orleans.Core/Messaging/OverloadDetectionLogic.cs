using Orleans.Configuration;
using Orleans.Statistics;

namespace Orleans.Core.Messaging;

internal static class OverloadDetectionLogic
{
    /// <summary>
    /// 确定进程是否过载。
    /// </summary>
    /// <remarks><see cref="LoadSheddingOptions.LoadSheddingEnabled"/> 在此处被忽略。</remarks>
    public static bool IsOverloaded(ref readonly EnvironmentStatistics statistics, LoadSheddingOptions options)
    {
        bool isMemoryOverloaded = statistics.MemoryUsagePercentage > options.MemoryThreshold;
        bool isCpuOverloaded = statistics.FilteredCpuUsagePercentage > options.CpuThreshold;

        return isMemoryOverloaded || isCpuOverloaded;
    }
}
