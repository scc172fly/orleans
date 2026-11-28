using System;
using System.Runtime.InteropServices;
using Orleans.Serialization;
using System.Diagnostics;

namespace Orleans.Statistics;

/// <summary>
/// 提供当前进程及其执行环境的统计信息。
/// </summary>
public interface IEnvironmentStatisticsProvider
{
    /// <summary>
    /// 获取当前环境统计信息；可能根据运行时配置进行筛选或处理。
    /// </summary>
    EnvironmentStatistics GetEnvironmentStatistics();
}

// 此结构体有意采用紧凑布局，以避免额外的填充字节。
// 此结构体会频繁创建，因此缩减其堆栈占用并降低序列化开销。
// 添加更多字段时，应注意字段布局不当可能产生大量空白空间。
/// <summary>
/// 包含当前进程及其执行环境的统计信息。
/// </summary>
[Immutable]
[GenerateSerializer]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[Alias("Orleans.Statistics.EnvironmentStatistics")]
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct EnvironmentStatistics : IEquatable<EnvironmentStatistics>
{
    /// <summary>
    /// 系统 CPU 使用率。
    /// <br/>
    /// 应用卡尔曼滤波平滑短期波动。
    /// 参见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>；
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>取值范围为 0.0 至 100.0。</remarks>
    [Id(0)]
    public readonly float FilteredCpuUsagePercentage;

    /// <summary>
    /// 进程当前占用的托管内存量。
    /// <br/>
    /// 应用卡尔曼滤波平滑短期波动。
    /// 参见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>；
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>
    /// 包括内存碎片，即托管堆中各对象之间未被使用的内存。
    /// </remarks>
    [Id(1)]
    public readonly long FilteredMemoryUsageBytes;

    /// <summary>
    /// 当前可供进程分配的内存量。
    /// <br/>
    /// 应用卡尔曼滤波平滑短期波动。
    /// 参见 <see href="https://en.wikipedia.org/wiki/Kalman_filter"/>；
    /// <see href="https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/"/>
    /// </summary>
    /// <remarks>
    /// 包括进程和系统当前可用的内存。
    /// </remarks>
    [Id(2)]
    public readonly long FilteredAvailableMemoryBytes;

    /// <summary>
    /// 进程可用的最大内存量。
    /// </summary>
    /// <remarks>
    /// 此值取以下两项中的较小值：
    /// <list type="bullet">
    /// <item><description>垃圾回收器开始积极回收内存的阈值，由 <see cref="GCMemoryInfo.HighMemoryLoadThresholdBytes"/> 定义。</description></item>
    /// <item><description>进程配置的内存限制，由 <see cref="GCMemoryInfo.TotalAvailableMemoryBytes"/> 定义。</description></item>
    /// </list>
    /// 容器化环境通常会设置内存限制。有关配置内存限制的详细信息，参见 <see href="https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-limit"/>。
    /// </remarks>
    [Id(3)]
    public readonly long MaximumAvailableMemoryBytes;

    /// <summary>
    /// 系统 CPU 使用率。
    /// </summary>
    /// <remarks>取值范围为 0.0 至 100.0。</remarks>
    [Id(4)]
    public readonly float RawCpuUsagePercentage;

    /// <summary>
    /// 进程当前占用的托管内存量。
    /// </summary>
    [Id(5)]
    public readonly long RawMemoryUsageBytes;

    /// <summary>
    /// 当前可供进程分配的内存量。
    /// </summary>
    [Id(6)]
    public readonly long RawAvailableMemoryBytes;

    /// <summary>
    /// 获取已用内存相对于当前可用内存的百分比，限制在 0 至 100 之间。
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
    /// 获取可用内存相对于当前已用内存的百分比，限制在 0 至 100 之间。
    /// </summary>
    /// <remarks>
    /// 值为 <c>0</c> 表示所有可用内存当前均已被使用。
    /// 值为 <c>100</c> 表示全部内存当前均可用。
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
    /// 获取归一化内存使用率（0.0 至 1.0）。
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
    /// 获取经滤波的归一化内存使用率（0.0 至 1.0）。
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
    /// 获取归一化可用内存比例（0.0 至 1.0）。
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
    /// 获取经滤波的归一化可用内存比例（0.0 至 1.0）。
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
    /// 判断两个环境统计信息快照是否相等。
    /// </summary>
    public static bool operator ==(EnvironmentStatistics left, EnvironmentStatistics right) => left.Equals(right);

    /// <summary>
    /// 判断两个环境统计信息快照是否不相等。
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
    /// 返回包含 CPU 和内存统计信息的字符串。
    /// </summary>
    /// <returns>包含 CPU 和内存统计信息的字符串。</returns>
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
