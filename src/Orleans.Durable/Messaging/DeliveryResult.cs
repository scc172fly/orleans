namespace Orleans.Durable.Messaging;

/// <summary>
/// 接收方对尝试向其收件箱投递消息的应答。
/// </summary>
/// <remarks>联合类型的分支标识符遵循声明顺序，并且是传输格式的一部分。</remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult")]
public union DeliveryResult(
    DeliveryResult.Accepted,
    DeliveryResult.Duplicate,
    DeliveryResult.Backpressured,
    DeliveryResult.HandlerNotFound,
    DeliveryResult.DeadLettered,
    DeliveryResult.Expired)
{
    /// <summary>接收方已持久化接受该消息。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Accepted")]
    public sealed class Accepted
    {
        /// <summary>获取共享结果。</summary>
        public static Accepted Instance { get; } = new();
    }

    /// <summary>收件箱已持有该消息或仍保留其处理记录。</summary>
    /// <remarks>
    /// 对于收件箱消息接收器已经拒绝的消息，其重复投递将收到原来的拒绝结果：
    /// 原始 <see cref="DeadLettered"/> 结果，包含拒绝原因和代码。
    /// </remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Duplicate")]
    public sealed class Duplicate
    {
        /// <summary>获取共享结果。</summary>
        public static Duplicate Instance { get; } = new();
    }

    /// <summary>收件箱已达到容量上限；发送方稍后重试。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Backpressured")]
    public sealed class Backpressured
    {
        /// <summary>获取共享结果。</summary>
        public static Backpressured Instance { get; } = new();
    }

    /// <summary>接收方收件箱没有已注册的处理程序。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.HandlerNotFound")]
    public sealed class HandlerNotFound
    {
        /// <summary>获取共享结果。</summary>
        public static HandlerNotFound Instance { get; } = new();
    }

    /// <summary>接收方永久拒绝了该消息。</summary>
    /// <param name="reason">用于诊断的拒绝原因。</param>
    /// <param name="code">标识接收方拒绝消息原因的代码（若有）。</param>
    /// <remarks>在其处理记录仍被保留期间再次投递时，将收到相同的结果。</remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.DeadLettered")]
    public sealed class DeadLettered(string reason, string? code = null)
    {
        /// <summary>获取用于诊断的拒绝原因。</summary>
        [Id(0)]
        public string Reason { get; } = reason;

        /// <summary>
        /// 获取标识接收方拒绝消息原因的代码，即由收件箱消息接收器提供的
        /// 应用自定义代码，例如 <c>"orders/duplicate-order"</c>；如果接收方没有报告代码，则结果为 <see langword="null"/>，
        /// 表示未报告任何代码。
        /// </summary>
        /// <remarks>
        /// 消息传递系统将该代码原样传递到接收方的处理记录和发送方的
        /// <see cref="OutboxMessageRetirement.Rejected.Code"/>，而不解释其含义。
        /// </remarks>
        [Id(1)]
        public string? Code { get; } = code;
    }

    /// <summary>
    /// 消息的发送时间不晚于接收方持久化记录的准入下限，并且接收方
    /// 未保留该消息的处理结果。
    /// </summary>
    /// <remarks>
    /// 此次尝试不会暂存任何消息效果。先前的尝试可能已被接受，因此发送方将
    /// 投递结果视为未知，并以 <see cref="OutboxMessageRetirement.DeadLettered"/> 状态终结该消息。
    /// </remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Expired")]
    public sealed class Expired
    {
        /// <summary>获取共享结果。</summary>
        public static Expired Instance { get; } = new();
    }
}
