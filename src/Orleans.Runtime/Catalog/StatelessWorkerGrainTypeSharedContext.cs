using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime;

/// <summary>
/// 职能由无国籍工人类型 grain 的所有情况分担。
/// </summary>
internal sealed class StatelessWorkerGrainTypeSharedContext
{
    public StatelessWorkerGrainTypeSharedContext(
        GrainTypeSharedContext shared,
        IOptions<StatelessWorkerOptions> statelessWorkerOptions)
    {
        Shared = shared;
        StatelessWorkerOptions = statelessWorkerOptions.Value;

        var placement = (StatelessWorkerPlacement)shared.PlacementStrategy;
        MaxLocalWorkers = placement.MaxLocal;
        RemoveIdleWorkers = placement.RemoveIdleWorkers && StatelessWorkerOptions.RemoveIdleWorkers;
        MinIdleCyclesBeforeRemoval = StatelessWorkerOptions.MinIdleCyclesBeforeRemoval > 0
            ? StatelessWorkerOptions.MinIdleCyclesBeforeRemoval
            : 1;
        IdleWorkersInspectionPeriod = StatelessWorkerOptions.IdleWorkersInspectionPeriod;
    }

    /// <summary>
    /// 获取通用 grain- 类型共享环境。
    /// </summary>
    public GrainTypeSharedContext Shared { get; }

    /// <summary>
    /// 获得无国籍工人的选择。
    /// </summary>
    public StatelessWorkerOptions StatelessWorkerOptions { get; }

    /// <summary>
    /// 获得允许此类 grain id 的最大允许的本地工人激活次数。
    /// </summary>
    public int MaxLocalWorkers { get; }

    /// <summary>
    /// 获得一个数值, 显示闲置工人是否应该主动清除 。
    /// </summary>
    public bool RemoveIdleWorkers { get; }

    /// <summary>
    /// 获得工人被撤职之前所需的连续无用的检查周期的最低数量。
    /// </summary>
    public int MinIdleCyclesBeforeRemoval { get; }

    /// <summary>
    /// 获得空闲工人检查之间的间隔时间。
    /// </summary>
    public TimeSpan IdleWorkersInspectionPeriod { get; }
}
