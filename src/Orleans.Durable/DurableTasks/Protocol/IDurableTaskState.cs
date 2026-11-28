using System;
using System.Collections.Generic;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskState
{
    /// <summary>
    /// 获取任务所记录的操作类型，由运行时在创建记录时设置。
    /// </summary>
    public DurableTaskRecordKind Kind { get; }

    /// <summary>
    /// 任务的结果；如果任务尚未完成，则为 <see langword="null"/>。
    /// </summary>
    public DurableTaskResponse? Result { get; }

    /// <summary>
    /// 需要接收此任务结果的其他 Grain 或本 Grain 的任务。
    /// </summary>
    /// <remarks>
    /// 调用方以用于寻址此任务的标识符订阅；通过
    /// <c>GetDurableTask</c> 等待此任务的持久化执行则以自己的子任务标识符订阅。位于其他 Grain 的订阅者
    /// 用传输标识符指定自身任务。终态任务在其他 Grain 上的每个订阅者
    /// 在发件箱中均有待处理的完成通知，直到发件箱将其退役；在此之前任务会得到保留。
    /// 本 Grain 上的订阅者在存储结果的同一次写入中收到结果。本地嵌套任务没有
    /// 订阅者。
    /// </remarks>
    public IReadOnlySet<DurableTaskSubscriber> CompletionDestinations { get; }

    /// <summary>
    /// 可调用请求。
    /// </summary>
    /// <exception cref="Exception">已存储的请求无法再解码，例如其类型已被删除。</exception>
    public IDurableTaskRequest? Request { get; }

    /// <summary>
    /// 在不解码请求的情况下，获取任务是否存储了请求。
    /// </summary>
    /// <remarks>
    /// 运行时依据此属性和任务类型对任务分类，因此无法再解码的已存储请求只会影响
    /// 需要解码它的操作，例如启动其任务。
    /// </remarks>
    public bool HasRequest { get; }

    /// <summary>
    /// 任务完成的时间。
    /// </summary>
    public DateTimeOffset? CompletedAt { get; }

    /// <summary>
    /// 请求取消的时间。
    /// </summary>
    public DateTimeOffset? CancellationRequestedAt { get; }

    /// <summary>
    /// 任务的取消请求携带的原因；若未请求取消，则为 <see langword="null"/>，
    /// 请求不携带原因时也是如此。
    /// </summary>
    /// <remarks>第一次取消请求决定原因。</remarks>
    public string? CancellationReason { get; }

    /// <summary>
    /// 获取任务的父任务是否以屏蔽保护方式启动它，使祖先的持久化取消不会传播到该任务。
    /// </summary>
    /// <remarks>任务自身的取消、执行树的中止及执行树的删除仍会作用于该任务。</remarks>
    public bool Shielded { get; }

    /// <summary>
    /// 任务创建的时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? DueTime { get; }

    public long ResumeGeneration { get; }

    /// <summary>
    /// 获取记录存储的指纹：被调用请求的可调用对象类型及序列化
    /// 参数的哈希；对于执行实例自身操作的记录，则记录其类型及标识，例如分离启动的
    /// 目标和根任务。
    /// </summary>
    /// <remarks>
    /// 请求参数的哈希用于诊断：它随参数的序列化形式变化，因此运行时从不
    /// 通过比较它判断重放或重试是否为同一操作，而由 <see cref="Method"/> 判断。
    /// </remarks>
    public string? RequestFingerprint { get; }

    /// <summary>
    /// 获取任务直接调用、通过对其他 Grain 的调用间接调用，或通过分离启动调用的接口方法的标识；
    /// 对于不调用任何方法的记录，则为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 标识是方法的可调用对象的格式化类型。到达该记录的重放，或者按根任务标识符
    /// 调度根任务的重试，只要具有记录的操作类型、目标及
    /// 方法，就属于同一操作；不比较参数。
    /// </remarks>
    public string? Method { get; }

    /// <summary>
    /// 获取终态结果因其完成通知转为死信而重新开始保留期的时间；或者
    /// <see langword="null"/> 表示保留期从 <see cref="CompletedAt"/> 开始。
    /// </summary>
    public DateTimeOffset? RetainedSince { get; }

    public GrainId RemoteTarget { get; }

    /// <summary>
    /// 获取记录对 <see cref="RemoteTarget"/> 的调用的可调用对象类型及序列化参数的哈希，
    /// 由诊断信息报告。
    /// </summary>
    /// <remarks>调用的标识是其目标及 <see cref="Method"/>，而不是这个哈希。</remarks>
    public string? RemoteRequestFingerprint { get; }

    /// <summary>
    /// 获取此记录在另一 Grain 上启动的任务所属 Grain 是否已经持久化接受该任务：
    /// <see langword="true"/>，或者启动失败：<see langword="false"/>，或者在尚未得知结果时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 持久化调用或分离任务的记录在其调用作为已投递退役时，或者
    /// 任务结果先到达时，记录接受状态。如果调用被目标拒绝（例如启动标识符
    /// 已经标识另一请求），或被发件箱转为死信，则启动失败，并将该失败存储为
    /// 记录的结果，除非该记录已经保存了自身的结果，例如取消结果。
    /// </remarks>
    public bool? Accepted { get; }

    /// <summary>
    /// 获取通过持久化消息传递创建此任务的 Grain。
    /// </summary>
    public GrainId CallerId { get; }

    /// <summary>
    /// 获取标识被调用任务本次运行的随机数，由宿主在首次存储任务的
    /// 调用时生成；对于非被调用任务，则为 <see cref="System.Guid.Empty"/>。
    /// </summary>
    /// <remarks>
    /// 任务执行树中的每个执行实例均以 <see cref="DurableTaskContext.Incarnation"/> 报告该值。此次运行被删除后，
    /// 相同标识符的后续运行具有另一个值。
    /// </remarks>
    public System.Guid Incarnation { get; }

    /// <summary>
    /// 获取任务终态结果在暂存时标记的 Grain 范围内的完成序列号；若任务没有结果，或者
    /// 其存储不对完成顺序排序，则为零。
    /// </summary>
    /// <remarks>
    /// 存储为每个已存储的结果标记大于所有已存储任务的序列号，该标记与
    /// 结果在同一次日志写入中提交，因此之后暂存的结果具有更大的序列号。选择最早完成任务的组合器（例如
    /// <c>WhenAny</c> 或 <c>WhenEach</c>）选择序列号最小的已完成候选任务，此选择在
    /// 每次重放中都相同。没有序列号的候选任务通过已记录的决策选择。
    /// </remarks>
    public long CompletionSequence { get; }
}
