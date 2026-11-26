using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 收到持久任务最终结果的 grain 的任务:任务本身下的该任务援引人
/// 或可持久处决儿童,但同意执行
/// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/>.
/// </summary>
/// <param name="GrainId">下 grain 并获得结果。</param>
/// <param name="TaskId">接受者 grain完成结果的任务</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.Protocol.DurableTaskSubscriber")]
internal readonly record struct DurableTaskSubscriber(
    [property: Id(0)] GrainId GrainId,
    [property: Id(1)] TaskId TaskId);
