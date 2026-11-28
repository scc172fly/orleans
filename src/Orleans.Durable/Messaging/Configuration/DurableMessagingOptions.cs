using System;

namespace Orleans.Durable.Messaging.Configuration;

/// <summary>
/// 持久化消息传递的收件箱、发件箱和消息泵的配置选项。
/// </summary>
/// <remarks>
/// <para>
/// 这些选项控制收件箱容量、去重记录的跟踪、收件箱和发件箱的重试及
/// 死信处理行为，以及消息泵的批量大小。
/// </para>
/// <para>
/// 传输采用至少一次投递。保留的已处理记录和持久化准入下限用于对逻辑发送去重。
/// 配置值影响投递可用性、内存占用、吞吐量和恢复特性。
/// </para>
/// </remarks>
public class DurableMessagingOptions
{
    internal const int MaximumBackoffExponent = 6;
    internal const int MaximumBackoffMultiplier = 1 << MaximumBackoffExponent;

    // 运行时将此基础延迟乘以一个不超过 MaximumBackoffMultiplier 的倍数。
    // 扩大后的延迟必须处于定时器实现所支持的 uint 毫秒数范围内。
    private static readonly TimeSpan MaxSupportedRetryDelay =
        TimeSpan.FromTicks(
            TimeSpan.FromMilliseconds(uint.MaxValue - 1).Ticks
            / MaximumBackoffMultiplier);

    /// <summary>
    /// 获取或设置收件箱中待处理消息的最大数量。
    /// 达到此上限时，新投递返回 <see cref="DeliveryResult.Backpressured"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 较小的值（例如 100）提供更强的背压，但可能降低吞吐量。
    /// 较大的值（例如 10,000）允许缓冲更多消息，但增加内存占用和恢复耗时。
    /// </para>
    /// <para>
    /// 接收新消息之前检查收件箱容量。消息持久化到
    /// 存储，因此容量限制既影响内存中的状态，也影响恢复期间的存储 I/O。
    /// </para>
    /// <para>
    /// 带有 <see cref="Envelope.DeliverAt"/> 时间的消息，从被接纳开始直到
    /// 处理完成都计入此限制，因此应根据 Grain 持有的已调度消息和正在处理的消息共同确定容量。
    /// </para>
    /// <para>
    /// Grain 发送给自身的消息不受此限制地被接纳，因为发送是同步操作。这些消息
    /// 在完成之前仍计入容量，因此可能使来自其他 Grain 的投递受到背压。
    /// </para>
    /// </remarks>
    /// <value>
    /// 收件箱最大容量。必须大于零，默认为 1000。
    /// </value>
    public int MaxCapacity { get; set; } = 1000;

    /// <summary>
    /// 获取或设置从消息发送时间起，接收收件箱保留其已处理记录并允许
    /// 投递该消息的时长。对仍有记录的已处理消息，返回 <see cref="DeliveryResult.Duplicate"/>；若
    /// 收件箱接收器在投递时拒绝了它，则返回相同的 <see cref="DeliveryResult.DeadLettered"/> 原因和代码。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 较长的窗口（例如 30 天）保留更多已处理记录并允许更早发送的消息被投递，从而增加
    /// 内存占用和存储 I/O。较短的窗口（例如 1 小时）使较早发送的消息更快过期。
    /// </para>
    /// <para>
    /// 收件箱将记入日志的准入下限推进至当前时钟减去此窗口；该下限只会向前推进，
    /// 并且在恢复和迁移后仍然有效。待处理消息保留至处理完成。已处理记录在其
    /// <see cref="InboxMessage.SentAt"/> 不晚于持久化下限时即可被移除。对于收件箱
    /// 未跟踪且发送时间不晚于下限的消息，返回 <see cref="DeliveryResult.Expired"/>，
    /// 即使时钟发生回拨也如此。收件箱绝不接纳发送时间晚于自身时钟加上
    /// 此窗口的投递：若该消息未被跟踪且高于准入下限，则拒绝投递，不暂存任何变更。
    /// Grain 向自身发送的消息采用自身时钟，若时钟回拨则采用回拨后高于下限的第一个时间。
    /// 因此，每条已处理记录最迟在接收方时钟的两个此类窗口内即可移除，
    /// 或在时钟回拨后的自发送场景中，于下限之后不久即可移除。
    /// </para>
    /// <para>
    /// 时钟跳变与偏差会改变消息过期时间和记录保留时长；去重依据
    /// 日志中的下限以及每次发送的固定时间。此窗口应比 <see cref="OutboxRetryTimeout"/> 更长，差值应足以容纳
    /// 应用允许的投递延迟和时钟变化，并在整个集群中统一配置。
    /// </para>
    /// </remarks>
    /// <value>
    /// 去重窗口。必须大于 <see cref="OutboxRetryTimeout"/>，默认为 7 天。
    /// </value>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 获取或设置发件箱投递失败或收件箱处理尝试失败后，首次重试前的延迟。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发件箱在每次投递尝试失败后将此间隔加倍，包括目标收件箱返回
    /// <see cref="DeliveryResult.Backpressured"/> 的情形，最大为此间隔的 64 倍。收件箱在
    /// 处理器失败后采用相同的退避策略。
    /// </para>
    /// <para>
    /// 较短的延迟（例如 100 毫秒）在目标快速处理消息时能够更快恢复，
    /// 但在持续背压下可能增加 CPU 占用。较长的延迟（例如 5 秒）
    /// 可以降低重试开销，但会增加消息投递延迟。
    /// </para>
    /// <para>
    /// 对于需要从背压中迅速恢复的高吞吐量场景，
    /// 可考虑 100 至 500 毫秒的值。对时延不太敏感的工作负载可采用 1 至 5 秒。
    /// </para>
    /// </remarks>
    /// <value>
    /// 初始重试间隔。必须大于零，默认为 1 秒。
    /// </value>
    public TimeSpan RetryInitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置收件箱对处理器尝试抛出异常的消息持续重试的时间，超过该时间后消息将
    /// 被转入死信。
    /// </summary>
    /// <remarks>
    /// 超时从首次失败尝试开始计时，到期时执行最后一次尝试。处理器异常
    /// 使消息的处理结果处于未知状态，因此重试可以跨越暂时性故障，例如服务中断和修复缺陷的部署；
    /// 处理器可调用 <c>IInboxHandlerContext.Fail</c> 立即将消息转入死信。正在重试的消息
    /// 保持其在 <see cref="MaxCapacity"/> 中的位置。超时与重试退避均依据 Durable Jobs 时钟，
    /// 该时钟为每次重试安排唤醒。
    /// </remarks>
    /// <value>
    /// 必须大于或等于 <see cref="TimeSpan.Zero"/>；取该值时，消息在第二次失败后转入死信：
    /// 恢复时无法区分失败的尝试与尚未运行就因崩溃中断的尝试。
    /// 默认为 6 小时。
    /// </value>
    public TimeSpan InboxRetryTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 获取或设置从消息发送时间起，发件箱尝试投递该消息的时长，到期后将其
    /// 转入死信，包括接收方施加背压或报告没有收件箱处理器的期间。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发件箱在暂存发送时将消息的 <see cref="InboxMessage.SentAt"/> 记入日志；重试、
    /// 背压和恢复均保留该时间。超时依据 Durable Jobs 时钟，时钟跳变可能使重试提前结束或
    /// 延长。超时或 <see cref="DeliveryResult.Expired"/> 意味着接收方此前的投递结果
    /// 未知。消息来源的 <see cref="IOutboxMessageSource.OnRetired"/> 将应用响应与
    /// 消息退出发件箱的操作原子地暂存；参见 <see cref="OutboxMessageRetirement.DeadLettered"/>。
    /// </para>
    /// <para>
    /// 发件箱将每个 <see cref="DeliveryResult.HandlerNotFound"/> 响应记录为错误。
    /// </para>
    /// </remarks>
    /// <value>
    /// 必须大于零且小于 <see cref="DeduplicationWindow"/>，以使重试预算在
    /// 接收方的保留窗口内耗尽。默认为 6 小时。
    /// </value>
    public TimeSpan OutboxRetryTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 获取或设置收件箱和发件箱死信的保留时长。
    /// </summary>
    public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// 获取或设置每个收件箱和每个发件箱保留死信的最大数量。
    /// </summary>
    public int MaxRetainedDeadLetters { get; set; } = 1000;

    /// <summary>
    /// 获取或设置消息泵单轮处理的收件箱消息最大数量。
    /// </summary>
    public int InboxBatchSize { get; set; } = 32;

    /// <summary>
    /// 获取或设置单个 Grain 同时在途的发件箱投递尝试最大数量。
    /// </summary>
    /// <remarks>
    /// 一次尝试完成后，消息泵立即启动另一次尝试，因此较慢的接收方只占用一个槽位，不会延迟
    /// 发往其他接收方的消息。
    /// </remarks>
    public int OutboxBatchSize { get; set; } = 32;

    /// <summary>
    /// 验证配置值，若有无效值则抛出异常。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 当 <see cref="MaxCapacity"/> 小于或等于零，
    /// 或 <see cref="DeduplicationWindow"/> 小于或等于 <see cref="TimeSpan.Zero"/>，
    /// 或任一重试、保留或批处理选项超出支持范围时抛出。
    /// </exception>

    /// <remarks>
    /// 依赖注入容器通常在注册服务时调用此方法，
    /// 以确保系统启动前配置值有效。
    /// </remarks>
    public void Validate()
    {

        if (MaxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCapacity), MaxCapacity, "MaxCapacity must be greater than zero.");
        }

        if (DeduplicationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DeduplicationWindow), DeduplicationWindow, "DeduplicationWindow must be greater than TimeSpan.Zero.");
        }

        if (RetryInitialInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryInitialInterval), RetryInitialInterval, "RetryInitialInterval must be greater than TimeSpan.Zero.");
        }
        if (RetryInitialInterval > MaxSupportedRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryInitialInterval),
                RetryInitialInterval,
                $"RetryInitialInterval must be less than or equal to {MaxSupportedRetryDelay}.");
        }

        if (InboxRetryTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InboxRetryTimeout), InboxRetryTimeout, "InboxRetryTimeout must be greater than or equal to TimeSpan.Zero.");
        }

        if (OutboxRetryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OutboxRetryTimeout), OutboxRetryTimeout, "OutboxRetryTimeout must be greater than TimeSpan.Zero.");
        }

        if (OutboxRetryTimeout >= DeduplicationWindow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OutboxRetryTimeout),
                OutboxRetryTimeout,
                "OutboxRetryTimeout must be less than DeduplicationWindow.");
        }

        if (DeadLetterRetentionPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DeadLetterRetentionPeriod),
                DeadLetterRetentionPeriod,
                "DeadLetterRetentionPeriod must be greater than TimeSpan.Zero.");
        }

        if (MaxRetainedDeadLetters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRetainedDeadLetters),
                MaxRetainedDeadLetters,
                "MaxRetainedDeadLetters must be greater than zero.");
        }

        if (InboxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InboxBatchSize), InboxBatchSize, "InboxBatchSize must be greater than zero.");
        }

        if (OutboxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutboxBatchSize), OutboxBatchSize, "OutboxBatchSize must be greater than zero.");
        }
    }
}
