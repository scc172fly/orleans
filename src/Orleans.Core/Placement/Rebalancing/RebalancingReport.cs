using System;
using System.Collections.Immutable;
using Orleans.Runtime;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// <see cref="IActivationRebalancerWorker"/>状态。
/// </summary>
[GenerateSerializer]
public enum RebalancerStatus : byte
{
    /// <summary>
    /// 它正在执行。
    /// </summary>
    Executing = 0,
    /// <summary>
    /// 暂停执行。
    /// </summary>
    Suspended = 1
}

/// <summary>
/// 启动再平衡器当前状态报告
/// </summary>
[GenerateSerializer, Immutable, Alias("RebalancingReport")]
public readonly struct RebalancingReport
{
    /// <summary>
    /// 重新平衡器目前所在的 silo 。
    /// </summary>
    [Id(0)] public required SiloAddress Host { get; init; }

    /// <summary>
    /// 重新平衡者的现状。
    /// </summary>
    [Id(1)] public required RebalancerStatus Status { get; init; }

    /// <summary>
    /// 重新平衡器暂停的时间( 如果有的话) 。
    /// </summary>
    /// <remarks>永远如此 <see langword="null"/> 如果 <see cref="Status"/> 是 <see cref="RebalancerStatus.Executing"/>.</remarks>
    [Id(2)] public TimeSpan? SuspensionDuration { get; init; }

    /// <summary>
    /// 目前对该组的不平衡的看法。
    /// </summary>
    /// <remarks>范围: [0-1]</remarks>
    [Id(3)] public required double ClusterImbalance { get; init; }

    /// <summary>
    /// 最新再平衡统计。
    /// </summary>
    [Id(4)] public required ImmutableArray<RebalancingStatistics> Statistics { get; init; }
}

/// <summary>
/// 给定 <see cref="SiloAddress"/>的重新平衡统计。
/// </summary>
/// <remarks>
/// 注意统计数字是近似值。</remarks>
[GenerateSerializer, Immutable, Alias("RebalancingStatistics")]
public readonly struct RebalancingStatistics
{
    /// <summary>
    /// 收集这些统计数据的时间。
    /// </summary>
    [Id(0)] public required DateTime TimeStamp { get; init; }

    /// <summary>
    /// 这些统计数字属于 silo 。
    /// </summary>
    [Id(1)] public required SiloAddress SiloAddress { get; init; }

    /// <summary>
    /// 迄今从这个 silo 中分散的大约启动次数。
    /// </summary>
    [Id(2)] public required ulong DispersedActivations { get; init; }

    /// <summary>
    /// 迄今为止,该 silo 获得的启动装置的大致数目。
    /// </summary>
    [Id(3)] public required ulong AcquiredActivations { get; init; }
}
