using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Runtime;

[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskInvocationMessage")]
internal sealed class DurableTaskInvocationMessage
{
    /// <summary>
    /// 获得被援引的通讯所要处理的任务:发送者随子子的线条识别器,即
    /// 发件人范围下的接收器储存,或接收人的根任务,用于独立祈祷或订阅。
    /// </summary>
    /// <remarks>见 <see cref="CallerScope"/>。</remarks>
    [Id(0)]
    public TaskId TaskId { get; init; }

    /// <summary>
    /// 获得接收器排期或 <see langword="null"/> 订阅时的订书请求,该订书带有
    /// 代替<see cref="SubscriberId"/> 。
    /// </summary>
    [Id(1)]
    public IDurableTaskRequest? Request { get; init; }

    [Id(2)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取是否引用启动一个分离的任务:独立根任务, 接收器保留这样的根任务
    /// 客户端已排定的根, 没有呼叫器 。
    /// </summary>
    [Id(3)]
    public bool Detached { get; init; }

    /// <summary>
    /// 获取发送者任务中的线条识别码,该信号是接收器任务的结果
    /// <see cref="TaskId"/>:独立援引开始的任务或现有任务,用于订阅
    /// 普通 grain 方法设置没有用户的单独指定职业。
    /// </summary>
    /// <remarks>
    /// 订户既未声称任务,也未确认其呼叫者的身份:接收人将它添加到任务完成时
    /// 目的地。
    /// </remarks>
    [Id(4)]
    public TaskId? SubscriberId { get; init; }
}

[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskCompletionMessage")]
internal sealed class DurableTaskCompletionMessage
{
    /// <summary>获取发送者的任务,其终端结果是电文随带。</summary>
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public required DurableTaskResponse Response { get; init; }

    [Id(2)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取接收器任务中接收结果完成的任务的线标识符,当它与
    /// <see cref="TaskId"/>:被接收人的儿童援引发送人的任务或签署该任务。
    /// </summary>
    [Id(3)]
    public TaskId? SubscriberId { get; init; }
}

[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskCancellationMessage")]
internal sealed class DurableTaskCancellationMessage
{
    /// <summary>
    /// 获取信件所地址的任务:发送者随子子的线条识别码,以便取消
    /// 或中止,或 <see cref="SubscriberId"/> 订阅的接收器的任务,以接受未订阅。
    /// </summary>
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取任务行政中止的原因,或合作取消的原因 <see langword="null"/>
    /// 请求。
    /// </summary>
    [Id(2)]
    public string? AbortReason { get; init; }

    /// <summary>
    /// 获得合作取消请求随附的理由,或无从随附 <see langword="null"/> 。
    /// </summary>
    [Id(3)]
    public string? CancellationReason { get; init; }

    /// <summary>
    /// 获取发送者任务中订阅 <see cref="TaskId"/> 信件的截止时间的电线识别码,或
    /// 取消或中止任务<see langword="null"/> 。结束订阅不会取消任务 。
    /// </summary>
    [Id(4)]
    public TaskId? SubscriberId { get; init; }
}

/// <summary>
/// 重新恢复耐久的延迟。 拥有 grain 的人将它发送到自己手中, 预定在延迟到期时进行 。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskResumeMessage")]
internal sealed class DurableTaskResumeMessage
{
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public long Generation { get; init; }

    [Id(2)]
    public GrainId Sender { get; init; }

    /// <summary>获得延迟处决树的化身, 即计划恢复的运行。</summary>
    [Id(3)]
    public Guid Incarnation { get; init; }
}

/// <summary>
/// 给援引的任务提供信号的价值,该任务记录每个信号名称的第一个值。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskSignalMessage")]
internal sealed class DurableTaskSignalMessage
{
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    /// <summary>获得维持信号值的成功响应。</summary>
    [Id(2)]
    public required DurableTaskResponse Value { get; init; }

    [Id(3)]
    public GrainId Sender { get; init; }
}

/// <summary>
/// 耐久 grain RPC电文主体及其有效载荷合同。
/// </summary>
/// <remarks>
/// 信封上没有发件人的身份,因此每个机构都携带发件人 grain。
/// </remarks>
internal sealed class DurableTaskMessageTypes(Serializer serializer)
{
    public const string SubjectPrefix = "durable-rpc/";
    public const string InvocationSubject = SubjectPrefix + "invoke";
    public const string CompletionSubject = SubjectPrefix + "complete";
    public const string CancellationSubject = SubjectPrefix + "cancel";
    public const string ResumeSubject = SubjectPrefix + "resume";
    public const string SignalSubject = SubjectPrefix + "signal";

    public MessageType<DurableTaskInvocationMessage> Invocation { get; } = new(InvocationSubject, serializer);

    public MessageType<DurableTaskCompletionMessage> Completion { get; } = new(CompletionSubject, serializer);

    public MessageType<DurableTaskCancellationMessage> Cancellation { get; } = new(CancellationSubject, serializer);

    public MessageType<DurableTaskResumeMessage> Resume { get; } = new(ResumeSubject, serializer);

    public MessageType<DurableTaskSignalMessage> Signal { get; } = new(SignalSubject, serializer);
}
