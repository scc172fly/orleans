using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 代表一项持久任务的开始, 启动人的发信箱未确认便发送
/// 结果,所以任务 grain 是否运行该任务是未知的。
/// </summary>
/// <remarks>
/// <para>
/// 发件箱是一份无法在通讯机 <c>OutboxRetryTimeout</c> 过期前确认的传票,或
/// 任务所在 grain 因为在进入的禁区已经过去,就因为已关闭而栅栏被围起来。
/// 达到 grain,它可能已经存储和运行了任务, 并且仅仅失去了确认。
/// 任务失败,但有以下例外:等待任务或等待接受的任务 <c>ScheduleAsync</c> 。
/// 发信箱的死信 持有要求检查。
/// </para>
/// <para>
/// 在根据另一个任务识别符重新开始工作之前,确定结果是否运行,
/// 例如,通过在任务 grain 上进行民意测验或检查其效果,使其副作用不高
/// 重复。
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskDeliveryException")]
public sealed class DurableTaskDeliveryException : Exception
{
    private DurableTaskDeliveryException()
    {
    }

    /// <summary>开始适用援引 <paramref name="taskId"/> 日期 时 <paramref name="target"/>.</summary>
    /// <param name="taskId">调用开始的任务。</param>
    /// <param name="target">下 grain 援引所针对的。</param>
    /// <param name="reason">死亡信的原因。</param>
    public DurableTaskDeliveryException(TaskId taskId, GrainId target, string reason)
        : base($"The delivery of durable task '{taskId}' to grain '{target}' wasn't confirmed, so whether the grain ran it is unknown: {reason}")
    {
        TaskId = taskId;
        Target = target;
    }

    /// <summary>获得调用开始的任务。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }

    /// <summary>得到 grain 援引所针对的。</summary>
    [Id(1)]
    public GrainId Target { get; private set; }
}
