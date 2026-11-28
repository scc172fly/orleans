using System;
using System.Collections.Immutable;
using Orleans.Runtime;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// <see cref="IActivationRebalancerWorker"/> 的状态。
/// </summary>
[GenerateSerializer]
public enum RebalancerStatus : byte
{
    /// <summary>
    /// 正在执行。
    /// </summary>
    Executing = 0,
    /// <summary>
    /// 已暂停。
    /// </summary>
    Suspended = 1
}

/// <summary>
/// 激活实例再均衡器当前状态的报告。
/// </summary>
[GenerateSerializer, Immutable, Alias("RebalancingReport")]
public readonly struct RebalancingReport
{
    /// <summary>
    /// 再均衡器当前所在的 Silo。
    /// </summary>
    [Id(0)] public required SiloAddress Host { get; init; }

    /// <summary>
    /// 再均衡器的当前状态。
    /// </summary>
    [Id(1)] public required RebalancerStatus Status { get; init; }

    /// <summary>
    /// 再均衡器暂停的时长（如果已暂停）。
    /// </summary>
    /// <remarks>此值始终为 <see langword="null"/>，前提是 <see cref="Status"/> 为 <see cref="RebalancerStatus.Executing"/>。</remarks>
    [Id(2)] public TimeSpan? SuspensionDuration { get; init; }

    /// <summary>
    /// 当前观测到的集群不均衡程度。
    /// </summary>
    /// <remarks>范围：[0-1]。</remarks>
    [Id(3)] public required double ClusterImbalance { get; init; }

    /// <summary>
    /// 最新的再均衡统计信息。
    /// </summary>
    [Id(4)] public required ImmutableArray<RebalancingStatistics> Statistics { get; init; }
}

/// <summary>
/// 指定 <see cref="SiloAddress"/> 的再均衡统计信息。
/// </summary>
/// <remarks>
/// 用于诊断和度量。注意：统计信息是近似值。</remarks>
[GenerateSerializer, Immutable, Alias("RebalancingStatistics")]
public readonly struct RebalancingStatistics
{
    /// <summary>
    /// 汇总这些统计信息的时间。
    /// </summary>
    [Id(0)] public required DateTime TimeStamp { get; init; }

    /// <summary>
    /// 这些统计信息所属的 Silo。
    /// </summary>
    [Id(1)] public required SiloAddress SiloAddress { get; init; }

    /// <summary>
    /// 截至目前从此 Silo 迁出的激活实例的近似数量。
    /// </summary>
    [Id(2)] public required ulong DispersedActivations { get; init; }

    /// <summary>
    /// 截至目前此 Silo 接收的激活实例的近似数量。
    /// </summary>
    [Id(3)] public required ulong AcquiredActivations { get; init; }
}
