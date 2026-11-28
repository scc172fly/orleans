using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 表示将在指定时间执行的持久化作业。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.DurableJob")]
public sealed class DurableJob
{
    /// <summary>
    /// 获取此持久化作业的唯一标识符。
    /// </summary>
    [Id(0)]
    public required string Id { get; init; }

    /// <summary>
    /// 获取持久化作业的名称。
    /// </summary>
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// 获取此作业应当执行的时间。
    /// </summary>
    [Id(2)]
    public DateTimeOffset DueTime { get; init; }

    /// <summary>
    /// 获取负责处理此作业的目标 Grain 的标识符。
    /// </summary>
    [Id(3)]
    public GrainId TargetGrainId { get; init; }

    /// <summary>
    /// 获取管理此持久化作业的分片的标识符。
    /// </summary>
    [Id(4)]
    public required string ShardId { get; init; }

    /// <summary>
    /// 获取与此持久化作业关联的可选元数据。
    /// </summary>
    [Id(5)]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 获取调度作业时捕获的 W3C <c>traceparent</c> 值。
    /// 用于在稍后执行作业时延续分布式跟踪。
    /// </summary>
    [Id(6)]
    public string? TraceParent { get; init; }

    /// <summary>
    /// 获取调度作业时捕获的 W3C <c>tracestate</c> 值。
    /// 用于在稍后执行作业时延续分布式跟踪。
    /// </summary>
    [Id(7)]
    public string? TraceState { get; init; }

    [Id(8)]
    [JsonInclude]
    internal long ExecutionGeneration { get; init; }

    internal DurableJob WithExecutionGeneration(long executionGeneration) =>
        new()
        {
            Id = Id,
            Name = Name,
            DueTime = DueTime,
            TargetGrainId = TargetGrainId,
            ShardId = ShardId,
            Metadata = Metadata,
            TraceParent = TraceParent,
            TraceState = TraceState,
            ExecutionGeneration = executionGeneration
        };
}
