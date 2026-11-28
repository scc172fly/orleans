using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 表示调度持久化作业的请求。
/// </summary>
public readonly struct ScheduleJobRequest
{
    /// <summary>
    /// 获取将接收持久化作业的目标 Grain 的标识符。
    /// </summary>
    public required GrainId Target { get; init; }

    /// <summary>
    /// 获取用于标识和处理程序路由的非空作业名称。
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// 获取作业应执行的日期和时间。
    /// </summary>
    public required DateTimeOffset DueTime { get; init; }

    /// <summary>
    /// 获取与作业关联的可选元数据。
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 获取与已调度作业关联的 W3C <c>traceparent</c> 值，用于在稍后执行作业时延续分布式跟踪。
    /// 如果为 <see langword="null"/>，则使用 <see cref="System.Diagnostics.Activity.Current"/> 在调用 <see cref="ILocalDurableJobManager.ScheduleJobAsync"/> 时的值。
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// 获取与已调度作业关联的 W3C <c>tracestate</c> 值，用于在稍后执行作业时延续分布式跟踪。
    /// 如果为 <see langword="null"/>，则使用 <see cref="System.Diagnostics.Activity.Current"/> 在调用 <see cref="ILocalDurableJobManager.ScheduleJobAsync"/> 时的值。
    /// </summary>
    public string? TraceState { get; init; }

    internal void Validate() =>
        ArgumentException.ThrowIfNullOrWhiteSpace(JobName, nameof(JobName));
}
