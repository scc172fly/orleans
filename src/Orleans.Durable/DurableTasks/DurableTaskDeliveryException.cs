using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 表示某个持久化任务的调用在启动方的发件箱中被转为死信且未确认
/// 结果，因此无法确定任务所属 Grain 是否运行过该任务。
/// </summary>
/// <remarks>
/// <para>
/// 如果发件箱在 Messaging 的 <c>OutboxRetryTimeout</c> 期限内无法确认调用，或者
/// 任务所属 Grain 因其准入截止时间已过而拒绝该调用，发件箱便将其转为死信。两种情况下，更早的一次尝试都可能
/// 已到达 Grain，Grain 可能已存储并运行任务，仅丢失了确认消息。启动方的任务记录
/// 因此异常而失败：等待任务，或者通过 <c>ScheduleAsync</c> 等待任务被接受时，都会抛出该异常。
/// 发件箱的死信记录保留该调用，供检查。
/// </para>
/// <para>
/// 应将结果视为未知。在以另一个任务标识符重新启动工作前，先确定该工作是否已运行，
/// 例如在任务所属 Grain 上轮询任务，或检查其产生的效果，以免
/// 重复执行其副作用。
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskDeliveryException")]
public sealed class DurableTaskDeliveryException : Exception
{
    private DurableTaskDeliveryException()
    {
    }

    /// <summary>为 <paramref name="taskId"/> 在 <paramref name="target"/> 上的调用初始化异常。</summary>
    /// <param name="taskId">该调用启动的任务。</param>
    /// <param name="target">调用所指向的 Grain。</param>
    /// <param name="reason">死信的原因。</param>
    public DurableTaskDeliveryException(TaskId taskId, GrainId target, string reason)
        : base($"The delivery of durable task '{taskId}' to grain '{target}' wasn't confirmed, so whether the grain ran it is unknown: {reason}")
    {
        TaskId = taskId;
        Target = target;
    }

    /// <summary>获取该调用启动的任务。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }

    /// <summary>获取调用所指向的 Grain。</summary>
    [Id(1)]
    public GrainId Target { get; private set; }
}
