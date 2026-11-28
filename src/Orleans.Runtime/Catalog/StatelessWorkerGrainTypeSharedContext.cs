using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime;

/// <summary>
/// 无状态工作者 Grain 类型的所有实例共享的功能。
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
    /// 获取通用的 Grain 类型级共享上下文。
    /// </summary>
    public GrainTypeSharedContext Shared { get; }

    /// <summary>
    /// 获取无状态工作者选项。
    /// </summary>
    public StatelessWorkerOptions StatelessWorkerOptions { get; }

    /// <summary>
    /// 获取此类型中每个 Grain 标识允许的本地工作者激活实例最大数量。
    /// </summary>
    public int MaxLocalWorkers { get; }

    /// <summary>
    /// 获取指示是否主动移除空闲工作者的值。
    /// </summary>
    public bool RemoveIdleWorkers { get; }

    /// <summary>
    /// 获取工作者可被移除前所需的最少连续空闲检查周期数。
    /// </summary>
    public int MinIdleCyclesBeforeRemoval { get; }

    /// <summary>
    /// 获取空闲工作者检查的间隔。
    /// </summary>
    public TimeSpan IdleWorkersInspectionPeriod { get; }
}
