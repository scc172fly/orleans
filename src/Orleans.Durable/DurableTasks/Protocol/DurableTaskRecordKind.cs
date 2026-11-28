using System.Text.Json.Serialization;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 已存储任务所记录的操作类型，由运行时在创建记录时设置。
/// </summary>
/// <remarks>
/// 重放会将它在某个标识符下请求的操作与已记录的类型比较，因此另一类型的操作会
/// 导致重放分歧，使该次执行尝试停滞，而不是观察或变更另一操作的记录。仅当
/// 针对 <see cref="EarlyCancellation"/> 记录的调用到达时，类型才会变化，成为其
/// <see cref="Invocation"/>。JSON 日志按名称存储类型。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.Protocol.DurableTaskRecordKind")]
[JsonConverter(typeof(JsonStringEnumConverter<DurableTaskRecordKind>))]
internal enum DurableTaskRecordKind : byte
{
    /// <summary>客户端或 Grain 作为请求调度的任务，它拥有自己的执行树。</summary>
    Invocation,

    /// <summary>先于其任务调用到达的取消请求。</summary>
    EarlyCancellation,

    /// <summary>持久化执行实例对另一个 Grain 的调用。</summary>
    RemoteCall,

    /// <summary>持久化执行实例的本地子任务，例如 <c>DurableTask.Run</c> 步骤。</summary>
    LocalStep,

    /// <summary>持久化延迟。</summary>
    Delay,

    /// <summary><c>DurableTask.RunInline</c> 效果的已记录结果。</summary>
    Effect,

    /// <summary>版本决策。</summary>
    Version,

    /// <summary>信号值，位于接收该信号的被调用任务之下。</summary>
    SignalValue,

    /// <summary>持久化执行实例发送信号的操作。</summary>
    SignalSend,

    /// <summary>选择组合器候选任务中最先完成的任务。</summary>
    Selection,

    /// <summary>对现有任务结果的订阅。</summary>
    Subscription,

    /// <summary>分离任务的启动操作，并订阅该任务的结果。</summary>
    DetachedStart,

    /// <summary>持久化执行实例的主体首次观察到其任务取消的取消点。</summary>
    CancellationObserved,
}
