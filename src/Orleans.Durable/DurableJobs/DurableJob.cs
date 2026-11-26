using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 代表将在特定时间执行的持久工作。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.DurableJob")]
public sealed class DurableJob
{
    /// <summary>
    /// 获得这项持久工作的独特身份识别符。
    /// </summary>
    [Id(0)]
    public required string Id { get; init; }

    /// <summary>
    /// 获得持久工作的名字。
    /// </summary>
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// 获得该任务执行的时间 。
    /// </summary>
    [Id(2)]
    public DateTimeOffset DueTime { get; init; }

    /// <summary>
    /// 获取目标 grain 的识别符,以用于处理此任务。
    /// </summary>
    [Id(3)]
    public GrainId TargetGrainId { get; init; }

    /// <summary>
    /// 获得管理这一持久工作的碎片识别码。
    /// </summary>
    [Id(4)]
    public required string ShardId { get; init; }

    /// <summary>
    /// 获取与这一持久工作相关的可选元数据。
    /// </summary>
    [Id(5)]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 获得 W3C <c>traceparent</c> 任务计划时捕获的值 。
    /// 用于继续分配工作完成后所留下的痕迹。
    /// </summary>
    [Id(6)]
    public string? TraceParent { get; init; }

    /// <summary>
    /// 获取工作计划时所捕捉的 W3C <c>tracestate</c> 值。
    /// 用于继续分配工作完成后所留下的痕迹。
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
