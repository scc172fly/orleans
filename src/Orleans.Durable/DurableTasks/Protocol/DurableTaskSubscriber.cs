using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 接收持久化任务终态结果的 Grain 任务：可以是以该任务自身的
/// 标识符标识的调用方，也可以是通过以下方式订阅现有任务的持久化执行实例的子任务：
/// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/>.
/// </summary>
/// <param name="GrainId">接收结果的 Grain。</param>
/// <param name="TaskId">接收方 Grain 中由此结果完成的任务。</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.Protocol.DurableTaskSubscriber")]
internal readonly record struct DurableTaskSubscriber(
    [property: Id(0)] GrainId GrainId,
    [property: Id(1)] TaskId TaskId);
