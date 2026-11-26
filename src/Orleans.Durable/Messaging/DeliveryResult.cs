namespace Orleans.Durable.Messaging;

/// <summary>
/// 接收者对试图将信息传送到收件箱的尝试的答复
/// </summary>
/// <remarks>案件识别特征遵循申报令,是电线格式的一部分。</remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult")]
public union DeliveryResult(
    DeliveryResult.Accepted,
    DeliveryResult.Duplicate,
    DeliveryResult.Backpressured,
    DeliveryResult.HandlerNotFound,
    DeliveryResult.DeadLettered,
    DeliveryResult.Expired)
{
    /// <summary>接收人长期接受电文。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Accepted")]
    public sealed class Accepted
    {
        /// <summary>获得共同的结果。</summary>
        public static Accepted Instance { get; } = new();
    }

    /// <summary>收件箱已经持有该信件或保留其处理过的记录。</summary>
    /// <remarks>
    /// 收件箱信息汇汇拒绝的电文的复制件,则接收到该回执:原件
    /// <see cref="DeadLettered"/> 其原理和代码。
    /// </remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Duplicate")]
    public sealed class Duplicate
    {
        /// <summary>获得共同的结果。</summary>
        public static Duplicate Instance { get; } = new();
    }

    /// <summary>收件箱处于状态;发送者稍后重试。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Backpressured")]
    public sealed class Backpressured
    {
        /// <summary>获得共同的结果。</summary>
        public static Backpressured Instance { get; } = new();
    }

    /// <summary>收件箱没有注册的处理器。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.HandlerNotFound")]
    public sealed class HandlerNotFound
    {
        /// <summary>获得共同的结果。</summary>
        public static HandlerNotFound Instance { get; } = new();
    }

    /// <summary>收件人永久拒绝该信件。</summary>
    /// <param name="reason">拒绝的诊断理由。</param>
    /// <param name="code">用以识别收件人拒绝电文的原因的代码。</param>
    /// <remarks>在其处理记录被保留期间重新交付,结果相同。</remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.DeadLettered")]
    public sealed class DeadLettered(string reason, string? code = null)
    {
        /// <summary>获得拒绝的诊断理由。</summary>
        [Id(0)]
        public string Reason { get; } = reason;

        /// <summary>
        /// 获取一个代码,用以识别接收者拒绝发送信件的原因:
        /// 收件箱信息汇,例如 <c>"orders/duplicate-order"</c>;接收器时的 <see langword="null"/>
        /// 无报告。
        /// </summary>
        /// <remarks>
        /// 通信将代码传送到接收器的处理记录和发送者的记录中
        /// <see cref="OutboxMessageRetirement.Rejected.Code"/> 没有解释它。
        /// </remarks>
        [Id(1)]
        public string? Code { get; } = code;
    }

    /// <summary>
    /// 电文是在接收人持久录取底楼层或之前发送的,接收人没有保留结果
    /// 因为它。
    /// </summary>
    /// <remarks>
    /// 尝试未发生电文效果。 早期的尝试可以被接受, 所以发送者会使用
    /// 将信息以未知的收件结果发送,并将信息以 <see cref="OutboxMessageRetirement.DeadLettered"/>形式发回。
    /// </remarks>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.DeliveryResult.Expired")]
    public sealed class Expired
    {
        /// <summary>获得共同的结果。</summary>
        public static Expired Instance { get; } = new();
    }
}
