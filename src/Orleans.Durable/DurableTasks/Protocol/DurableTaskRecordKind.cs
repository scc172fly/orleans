using System.Text.Json.Serialization;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 存储任务记录时的操作类型, 当运行时间创建该记录时设置它 。
/// </summary>
/// <remarks>
/// 重放比照其请求在识别特征下进行的操作与记录的类型,因此
/// 另一种类型的差异,它阻碍尝试,而不是观察或改变另一类操作的记录。
/// 只有当一个祈祷到达可成为其记录的 <see cref="EarlyCancellation"/> 记录时,才会更改该记录。
/// <see cref="Invocation"/>. JSON 期刊以名称存储该类。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Protocol.DurableTaskRecordKind")]
[JsonConverter(typeof(JsonStringEnumConverter<DurableTaskRecordKind>))]
internal enum DurableTaskRecordKind : byte
{
    /// <summary>一项任务,即客户或 grain 请求中注明了执行树的产地。</summary>
    Invocation,

    /// <summary>是在援引其任务之前取消的。</summary>
    EarlyCancellation,

    /// <summary>持久处决对另一个人的号召 grain.</summary>
    RemoteCall,

    /// <summary>持久处决的当地儿童,例如 <c>DurableTask.Run</c> 步骤。</summary>
    LocalStep,

    /// <summary>持久拖延。</summary>
    Delay,

    /// <summary>记录的结果 <c>DurableTask.RunInline</c> 效果。</summary>
    Effect,

    /// <summary>一份决定的文本。</summary>
    Version,

    /// <summary>信号的价值, 在所援引的任务之下 接收它。</summary>
    SignalValue,

    /// <summary>持久处决的信号发出</summary>
    SignalSend,

    /// <summary>选择在组合候选人中完成的第一个任务。</summary>
    Selection,

    /// <summary>订阅一项现有任务的结果。</summary>
    Subscription,

    /// <summary>独立任务的开始, 且该任务同意任务结果。</summary>
    DetachedStart,

    /// <summary>持久执行机关首次观察其取消任务时的取消点。</summary>
    CancellationObserved,
}
