using System;

namespace Orleans.Configuration;

/// <summary>
/// 全局应用于无状态工作者 Grain 的选项。
/// </summary>
public class StatelessWorkerOptions
{
    /// <summary>
    /// 设为 <see langword="true"/> 时，运行时会主动停用空闲工作者。
    /// 否则，如果为 <see langword="false"/>，则根据 <see cref="GrainCollectionOptions.CollectionAge"/> 停用工作者。
    /// </summary>
    /// <remarks>有关此项的详细信息请参阅<see href="https://www.ledjonbehluli.com/posts/orleans_adaptive_stateless_worker/">此处</see></remarks>
    public bool RemoveIdleWorkers { get; set; } = DEFAULT_REMOVE_IDLE_WORKERS;

    /// <summary>
    /// <see cref="RemoveIdleWorkers"/> 的默认值。
    /// </summary>
    public const bool DEFAULT_REMOVE_IDLE_WORKERS = true;

    /// <summary>
    /// 检查空闲工作者的时间间隔。
    /// </summary>
    /// <remarks>如果 <see cref="RemoveIdleWorkers"/> 为 <see langword="false"/>，此设置不起作用。</remarks>
    public TimeSpan IdleWorkersInspectionPeriod { get; set; } = DEFAULT_IDLE_WORKERS_INSPECTION_PERIOD;

    /// <summary>
    /// <see cref="IdleWorkersInspectionPeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_IDLE_WORKERS_INSPECTION_PERIOD = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 判定可以移除工作者前，任意给定工作者必须表现为连续空闲的最少检查轮次数。
    /// </summary>
    public int MinIdleCyclesBeforeRemoval { get; set; } = DEFAULT_MIN_IDLE_CYCLES_BEFORE_REMOVAL;

    /// <summary>
    /// <see cref="MinIdleCyclesBeforeRemoval"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MIN_IDLE_CYCLES_BEFORE_REMOVAL = 1;
}
