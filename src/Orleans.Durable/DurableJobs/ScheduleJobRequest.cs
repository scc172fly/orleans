using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 系要求安排一个耐用工作。
/// </summary>
public readonly struct ScheduleJobRequest
{
    /// <summary>
    /// 得到 grain 目标标识符 grain 且得续聘的工作。
    /// </summary>
    public required GrainId Target { get; init; }

    /// <summary>
    /// 获取非空白的工作名称,以便识别身份和处理者路线。
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// 获得执行该任务的日期和时间。
    /// </summary>
    public required DateTimeOffset DueTime { get; init; }

    /// <summary>
    /// 获取与该工作相关的可选元数据。
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 获得 W3C <c>traceparent</c> 以与计划的工作挂钩,用于在任务执行后继续分布跟踪。
    /// 如果 <see langword="null"/>,值, <see cref="System.Diagnostics.Activity.Current"/> 时间时 <see cref="ILocalDurableJobManager.ScheduleJobAsync"/> 将使用援引。
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// 获取 W3C <c>tracestate</c> 值与计划的工作挂钩,用于在工作执行后继续分配跟踪。
    /// 如果 <see langword="null"/>,值, <see cref="System.Diagnostics.Activity.Current"/> 时间时 <see cref="ILocalDurableJobManager.ScheduleJobAsync"/> 将使用援引。
    /// </summary>
    public string? TraceState { get; init; }

    internal void Validate() =>
        ArgumentException.ThrowIfNullOrWhiteSpace(JobName, nameof(JobName));
}
