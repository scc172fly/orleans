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
    /// 获取调用所寻址的任务：对于附接调用，为发送方附接子任务的传输标识符，由
    /// 接收方将其存储在发送方的作用域中；对于分离调用或订阅，则为接收方的根任务。
    /// </summary>
    /// <remarks>参见 <see cref="CallerScope"/>。</remarks>
    [Id(0)]
    public TaskId TaskId { get; init; }

    /// <summary>
    /// 获取接收方要调度的请求；对于订阅，则为 <see langword="null"/>，此时改为携带
    /// <see cref="SubscriberId"/>。
    /// </summary>
    [Id(1)]
    public IDurableTaskRequest? Request { get; init; }

    [Id(2)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取该调用是否启动分离任务：独立的根任务，接收方以与客户端调度的
    /// 根任务相同的方式保留它，且该任务没有调用方。
    /// </summary>
    [Id(3)]
    public bool Detached { get; init; }

    /// <summary>
    /// 获取发送方用于订阅接收方任务结果的任务的传输标识符，接收方任务由
    /// <see cref="TaskId"/> 指定：对于分离调用，是该调用启动的任务；对于不携带请求的订阅，是现有
    /// 任务。普通 Grain 方法投递的不含订阅者的分离调用没有此标识符。
    /// </summary>
    /// <remarks>
    /// 订阅者既不声明任务的调用方身份，也不验证其调用方；接收方将其添加到任务的完成通知
    /// 目的地。
    /// </remarks>
    [Id(4)]
    public TaskId? SubscriberId { get; init; }
}

[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskCompletionMessage")]
internal sealed class DurableTaskCompletionMessage
{
    /// <summary>获取消息所携带终态结果对应的发送方任务。</summary>
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public required DurableTaskResponse Response { get; init; }

    [Id(2)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取由该结果完成的接收方任务的传输标识符，前提是它不同于
    /// <see cref="TaskId"/>；该任务为调用发送方任务的接收方子任务，或者订阅了发送方任务的任务。
    /// </summary>
    [Id(3)]
    public TaskId? SubscriberId { get; init; }
}

[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskCancellationMessage")]
internal sealed class DurableTaskCancellationMessage
{
    /// <summary>
    /// 获取消息所寻址的任务：对于取消或中止，为发送方附接子任务的传输标识符；对于取消
    /// 订阅，为接收方的、<see cref="SubscriberId"/> 所订阅的任务。
    /// </summary>
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public GrainId Sender { get; init; }

    /// <summary>
    /// 获取任务被管理性中止的原因，或者 <see langword="null"/>（对于协作式取消
    /// 请求）。
    /// </summary>
    [Id(2)]
    public string? AbortReason { get; init; }

    /// <summary>
    /// 获取协作式取消请求携带的原因；若未携带原因，则为 <see langword="null"/>。
    /// </summary>
    [Id(3)]
    public string? CancellationReason { get; init; }

    /// <summary>
    /// 获取发送方任务的传输标识符，本消息结束该任务对 <see cref="TaskId"/> 的订阅；对于
    /// 任务的取消或中止，则为 <see langword="null"/>。结束订阅不会取消任务。
    /// </summary>
    [Id(4)]
    public TaskId? SubscriberId { get; init; }
}

/// <summary>
/// 恢复持久化延迟。所属 Grain 将此消息发给自身，并将其计划在延迟到期时投递。
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

    /// <summary>获取该延迟所属执行树的运行世代标识，即调度恢复操作的那次运行。</summary>
    [Id(3)]
    public Guid Incarnation { get; init; }
}

/// <summary>
/// 将信号值投递给被调用的任务，该任务记录每个信号名称的第一个值。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskSignalMessage")]
internal sealed class DurableTaskSignalMessage
{
    [Id(0)]
    public TaskId TaskId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    /// <summary>获取保存信号值的成功响应。</summary>
    [Id(2)]
    public required DurableTaskResponse Value { get; init; }

    [Id(3)]
    public GrainId Sender { get; init; }
}

/// <summary>
/// 持久化 Grain RPC 的消息主题及其有效载荷契约。
/// </summary>
/// <remarks>
/// Messaging 的消息信封不携带发送方标识，因此每个消息正文都携带发送方 Grain。
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
