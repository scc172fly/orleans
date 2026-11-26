using System;
using System.Runtime.InteropServices;
using Orleans.Serialization;
using System.Diagnostics;

namespace Orleans.Statistics;

/// <summary>
/// 提供关于当前进程及其执行环境的统计数据。
/// </summary>
public interface IEnvironmentStatisticsProvider
{
    /// <summary>
    /// 获取当前环境统计。 可能根据运行时间配置应用过滤或处理程序 。
    /// </summary>
    EnvironmentStatistics GetEnvironmentStatistics();
}

// 为了避免额外加盖, 此支架是故意“ 包装” 的 。
// 它将非常频繁地生成, 所以我们会减少堆叠大小,降低序列成本。
// 随着更多的领域被添加到这一层,它们可以被置于这样的位置上,从而可能导致许多“空地”空间。
/// <summary>
/// 载有关于当前过程及其执行环境的统计数据。
/// </summary>
[Immutable]
[GenerateSerializer]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[Alias("Orleans.Statistics.EnvironmentStatistics")]
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct EnvironmentStatistics : IEquatable<EnvironmentStatistics>
{
    /// <summary>
    /// CPU 系统使用率。
    /// <br/>
    /// 应用 Kalman 过滤来平息短期波动 。
    /// 见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>;
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>范围为0.0-100.0。</remarks>
    [Id(0)]
    public readonly float FilteredCpuUsagePercentage;

    /// <summary>
    /// 程序目前消耗的管理内存数量。
    /// <br/>
    /// 应用 Kalman 过滤来平息短期波动 。
    /// 见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>;
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>
    /// 包括零碎的内存,即管理堆积上的物体之间未使用的内存。
    /// </remarks>
    [Id(1)]
    public readonly long FilteredMemoryUsageBytes;

    /// <summary>
    /// 目前可用于分配给这一进程的内存数量。
    /// <br/>
    /// 应用 Kalman 过滤来平息短期波动 。
    /// 见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>;
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>
    /// 包括目前可用的程序和系统的内存。
    /// </remarks>
    [Id(2)]
    public readonly long FilteredAvailableMemoryBytes;

    /// <summary>
    /// 进程可用的最大内存量。
    /// </summary>
    /// <remarks>
    /// 此值以两笔数额的较低者计算:
    /// <list type="bullet">
    ///   <item><description>垃圾收集器开始积极收集 <see cref="GCMemoryInfo.HighMemoryLoadThresholdBytes"/>定义的内存量。</description></item>
    ///   <item><description>进程配置的内存限制,由 <see cref="GCMemoryInfo.TotalAvailableMemoryBytes"/>定义。</description></item>
    /// </list>
    /// 内存限值在集装箱化环境中很常见。关于配置内存限值的更多信息,请参见 <see href="https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-limit"/>
    /// </remarks>
    [Id(3)]
    public readonly long MaximumAvailableMemoryBytes;

    /// <summary>
    /// CPU 系统使用率。
    /// </summary>
    /// <remarks>范围为0.0-100.0。</remarks>
    [Id(4)]
    public readonly float RawCpuUsagePercentage;

    /// <summary>
    /// 程序目前消耗的管理内存数量。
    /// </summary>
    [Id(5)]
    public readonly long RawMemoryUsageBytes;

    /// <summary>
    /// 目前可用于分配给这一进程的内存数量。
    /// </summary>
    [Id(6)]
    public readonly long RawAvailableMemoryBytes;

    /// <summary>
    /// 获得相对于现有可用内存的记忆使用的百分比,内存在0至100之间。
    /// </summary>
    public float MemoryUsagePercentage
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var percent = (double)RawMemoryUsageBytes / MaximumAvailableMemoryBytes * 100.0;
            return (float)Math.Clamp(percent, 0.0, 100.0);
        }
    }

    /// <summary>
    /// 获得可用内存相对于当前使用的内存的百分比,该内存在0到100之间。
    /// </summary>
    /// <remarks>
    /// <c>0</c> 表示所有可用内存目前都在使用。
    /// 值 <c>100</c> 表示所有内存目前都可用。
    /// </remarks>
    public float AvailableMemoryPercentage
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var percent = (double)RawAvailableMemoryBytes / MaximumAvailableMemoryBytes * 100.0;
            return (float)Math.Clamp(percent, 0.0, 100.0);
        }
    }

    /// <summary>
    /// 获得正常内存使用( 0.0 到 1. 0) 。
    /// </summary>
    public float NormalizedMemoryUsage
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var fraction = (double)RawMemoryUsageBytes / MaximumAvailableMemoryBytes;
            return (float)Math.Clamp(fraction, 0.0, 1.0);
        }
    }

    /// <summary>
    /// 获取已标准化过滤的内存使用量( 0.0 到 1. 0) 。
    /// </summary>
    public float NormalizedFilteredMemoryUsage
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var fraction = (double)FilteredMemoryUsageBytes / MaximumAvailableMemoryBytes;
            return (float)Math.Clamp(fraction, 0.0, 1.0);
        }
    }

    /// <summary>
    /// 获取可使用的常规内存( 0.0 到 1. 0) 。
    /// </summary>
    public float NormalizedAvailableMemory
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var fraction = (double)RawAvailableMemoryBytes / MaximumAvailableMemoryBytes;
            return (float)Math.Clamp(fraction, 0.0, 1.0);
        }
    }

    /// <summary>
    /// 获取已归正的过滤可用内存( 0.0 到 1. 0) 。
    /// </summary>
    public float NormalizedFilteredAvailableMemory
    {
        get
        {
            if (MaximumAvailableMemoryBytes <= 0) return 0f;
            var fraction = (double)FilteredAvailableMemoryBytes / MaximumAvailableMemoryBytes;
            return (float)Math.Clamp(fraction, 0.0, 1.0);
        }
    }

    /// <inheritdoc/>
    public bool Equals(EnvironmentStatistics other) =>
        FilteredCpuUsagePercentage.Equals(other.FilteredCpuUsagePercentage)
        && FilteredMemoryUsageBytes == other.FilteredMemoryUsageBytes
        && FilteredAvailableMemoryBytes == other.FilteredAvailableMemoryBytes
        && MaximumAvailableMemoryBytes == other.MaximumAvailableMemoryBytes
        && RawCpuUsagePercentage.Equals(other.RawCpuUsagePercentage)
        && RawMemoryUsageBytes == other.RawMemoryUsageBytes
        && RawAvailableMemoryBytes == other.RawAvailableMemoryBytes;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EnvironmentStatistics other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            FilteredCpuUsagePercentage,
            FilteredMemoryUsageBytes,
            FilteredAvailableMemoryBytes,
            MaximumAvailableMemoryBytes,
            RawCpuUsagePercentage,
            RawMemoryUsageBytes,
            RawAvailableMemoryBytes);

    /// <summary>
    /// 确定两个环境统计快照是否相等。
    /// </summary>
    public static bool operator ==(EnvironmentStatistics left, EnvironmentStatistics right) => left.Equals(right);

    /// <summary>
    /// 确定两个环境统计快照是否不平等。
    /// </summary>
    public static bool operator !=(EnvironmentStatistics left, EnvironmentStatistics right) => !left.Equals(right);

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        if (bytes >= GB)
            return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB)
            return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB)
            return $"{bytes / (double)KB:F2} KB";
        return $"{bytes} B";
    }

    /// <summary>
    /// 返回含有 CPU 和内存统计的字符串。
    /// </summary>
    /// <returns>包含 CPU 和内存统计的字符串。</returns>
    public override string ToString()
        => $"CpuUsage: {FilteredCpuUsagePercentage:F2}% (raw: {RawCpuUsagePercentage:F2}%) | " +
           $"MemoryUsage: {FormatBytes(FilteredMemoryUsageBytes)} (raw: {FormatBytes(RawMemoryUsageBytes)}) [{MemoryUsagePercentage:F2}%] | " +
           $"AvailableMemory: {FormatBytes(FilteredAvailableMemoryBytes)} (raw: {FormatBytes(RawAvailableMemoryBytes)}) [{AvailableMemoryPercentage:F2}%] | " +
           $"MaximumAvailableMemory: {FormatBytes(MaximumAvailableMemoryBytes)}";

    internal EnvironmentStatistics(
        float cpuUsagePercentage,
        float rawCpuUsagePercentage,
        long memoryUsageBytes,
        long rawMemoryUsageBytes,
        long availableMemoryBytes,
        long rawAvailableMemoryBytes,
        long maximumAvailableMemoryBytes)
    {
        FilteredCpuUsagePercentage = Math.Clamp(cpuUsagePercentage, 0f, 100f);
        RawCpuUsagePercentage = Math.Clamp(rawCpuUsagePercentage, 0f, 100f);
        FilteredMemoryUsageBytes = memoryUsageBytes;
        RawMemoryUsageBytes = rawMemoryUsageBytes;
        FilteredAvailableMemoryBytes = availableMemoryBytes;
        RawAvailableMemoryBytes = rawAvailableMemoryBytes;
        MaximumAvailableMemoryBytes = maximumAvailableMemoryBytes;

#if DEBUG
        Debug.Assert(MaximumAvailableMemoryBytes >= 0, $"{nameof(MaximumAvailableMemoryBytes)} must be non-negative. {this}");
        Debug.Assert(RawMemoryUsageBytes >= 0, $"{nameof(RawMemoryUsageBytes)} must be non-negative. {this}");
        Debug.Assert(RawAvailableMemoryBytes >= 0, $"{nameof(RawAvailableMemoryBytes)} must be non-negative. {this}");
        Debug.Assert(RawMemoryUsageBytes + RawAvailableMemoryBytes <= MaximumAvailableMemoryBytes,
            $"Sum of {nameof(RawMemoryUsageBytes)} and {nameof(RawAvailableMemoryBytes)} must not exceed {nameof(MaximumAvailableMemoryBytes)}. {this}");

        Debug.Assert(FilteredMemoryUsageBytes >= 0, $"{nameof(FilteredMemoryUsageBytes)} must be non-negative. {this}");
        Debug.Assert(FilteredAvailableMemoryBytes >= 0, $"{nameof(FilteredAvailableMemoryBytes)} must be non-negative. {this}");

        Debug.Assert(RawCpuUsagePercentage is >= 0.0f and <= 100.0f, $"{nameof(RawCpuUsagePercentage)} must be between 0.0 and 100.0. {this}");
        Debug.Assert(FilteredCpuUsagePercentage is >= 0.0f and <= 100.0f, $"{nameof(FilteredCpuUsagePercentage)} must be between 0.0 and 100.0. {this}");

        Debug.Assert(MemoryUsagePercentage is >= 0.0f and <= 100.0f, $"{nameof(MemoryUsagePercentage)} must be between 0.0 and 100.0. {this}");
        Debug.Assert(AvailableMemoryPercentage is >= 0.0f and <= 100.0f, $"{nameof(AvailableMemoryPercentage)} must be between 0.0 and 100.0. {this}");
        Debug.Assert(NormalizedMemoryUsage is >= 0.0f and <= 1.0f, $"{nameof(NormalizedMemoryUsage)} must be between 0.0 and 1.0. {this}");
        Debug.Assert(NormalizedAvailableMemory is >= 0.0f and <= 1.0f, $"{nameof(NormalizedAvailableMemory)} must be between 0.0 and 1.0. {this}");
        Debug.Assert(NormalizedFilteredMemoryUsage is >= 0.0f and <= 1.0f, $"{nameof(NormalizedFilteredMemoryUsage)} must be between 0.0 and 1.0. {this}");
        Debug.Assert(NormalizedFilteredAvailableMemory is >= 0.0f and <= 1.0f, $"{nameof(NormalizedFilteredAvailableMemory)} must be between 0.0 and 1.0. {this}");
#endif
    }
}
