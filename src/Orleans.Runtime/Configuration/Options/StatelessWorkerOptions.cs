using System;

namespace Orleans.Configuration;

/// <summary>
/// 在全球范围内适用于无国籍工人的备选办法 grains。
/// </summary>
public class StatelessWorkerOptions
{
    /// <summary>
    /// 当设定为 <see langword="true"/>时,闲置工人将在运行时间前主动停止工作。
    /// 否则,如果 <see langword="false"/>,超过工人的根据 <see cref="GrainCollectionOptions.CollectionAge"/>将停止工作。
    /// </summary>
    /// <remarks>您可以在此读更多有关 <see href="https://www.ledjonbehluli.com/posts/orleans_adaptive_stateless_worker/">的</see></remarks>
    public bool RemoveIdleWorkers { get; set; } = DEFAULT_REMOVE_IDLE_WORKERS;

    /// <summary>
    /// <see cref="RemoveIdleWorkers"/>的默认值。
    /// </summary>
    public const bool DEFAULT_REMOVE_IDLE_WORKERS = true;

    /// <summary>
    /// 检查闲置工人的时间。
    /// </summary>
    /// <remarks>如果 <see cref="RemoveIdleWorkers"/> 是 <see langword="false"/>.</remarks>
    public TimeSpan IdleWorkersInspectionPeriod { get; set; } = DEFAULT_IDLE_WORKERS_INSPECTION_PERIOD;

    /// <summary>
    /// <see cref="IdleWorkersInspectionPeriod"/>的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_IDLE_WORKERS_INSPECTION_PERIOD = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 任何工人必须先展示闲置周期的最低连续数,然后才能认为该工已足以让工人离开。
    /// </summary>
    public int MinIdleCyclesBeforeRemoval { get; set; } = DEFAULT_MIN_IDLE_CYCLES_BEFORE_REMOVAL;

    /// <summary>
    /// <see cref="MinIdleCyclesBeforeRemoval"/>的默认值。
    /// </summary>
    public const int DEFAULT_MIN_IDLE_CYCLES_BEFORE_REMOVAL = 1;
}
