using System;

namespace Orleans.Durable.Messaging.Configuration;

/// <summary>
/// 用于耐用信息信箱、发件箱和短信泵的配置选项。
/// </summary>
/// <remarks>
/// <para>
/// 这些选项控制箱容量、重复跟踪、箱和发件箱重试
/// 和泵批量尺寸。
/// </para>
/// <para>
/// 保存处理的记录和持久入场底层的复杂逻辑发送。
/// 配置值影响交付的可用性、记忆使用、吞吐量和回收特性。
/// </para>
/// </remarks>
public class DurableMessagingOptions
{
    internal const int MaximumBackoffExponent = 6;
    internal const int MaximumBackoffMultiplier = 1 << MaximumBackoffExponent;

    // 运行时将基准延迟乘以最多 MaximumBackoffMultiplier。
    // 将扩大的延迟保持在时间执行的五毫米限内。
    private static readonly TimeSpan MaxSupportedRetryDelay =
        TimeSpan.FromTicks(
            TimeSpan.FromMilliseconds(uint.MaxValue - 1).Ticks
            / MaximumBackoffMultiplier);

    /// <summary>
    /// 获取或设定待发信箱信件的最大数量 。
    /// 当达到这一限额时,新交货返回 <see cref="DeliveryResult.Backpressured"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 低值(e.g.,100)提供较强的后压,但可能会减少吞吐量。
    /// 数值越高(e.g, 10,000),可以使更多的缓冲,但增加内存的使用和恢复时间。
    /// </para>
    /// <para>
    /// 接收新信件前先检查收件箱容量。 信件会持续到耐用
    /// 容量限制既影响模拟状态,也影响恢复期间的储存I/O/I/O。
    /// </para>
    /// <para>
    /// 带有 <see cref="Envelope.DeliverAt"/> 时间的电文从接受到接收,其时间数都指向此限制
    /// 完整,因此要对预定的 grain 搁置以及正在处理的信件的限度大小。
    /// </para>
    /// <para>
    /// 不论此限制,自发的 grain 信件被接受,因为发送同步。它们计数
    /// 至于完成,而从别的牲畜中取出粮食, grains 以至后退。
    /// </para>
    /// </remarks>
    /// <value>
    /// 最大收件箱容量。 必须大于零, 默认为1000 。
    /// </value>
    public int MaxCapacity { get; set; } = 1000;

    /// <summary>
    /// 获取或设定信息发送时间后多久接收收件箱保留其处理过的记录并承认
    /// 跟踪处理的电文以 <see cref="DeliveryResult.Duplicate"/>回答,或当
    /// 投递时, 收件箱槽拒绝发送, 与 <see cref="DeliveryResult.DeadLettered"/> 理性和代码。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 较长的窗口(e.g.,30天)保留处理较多的记录并承认旧发件,增加
    /// 短窗口(e.g., 1小时)过期时间较早。
    /// </para>
    /// <para>
    /// 箱箱将日记录取层提前到当前时钟时点减去此窗口;仅提前
    /// 处理完毕的档案记录
    /// 一旦 <see cref="InboxMessage.SentAt"/> 位于或低于耐久底线,即有资格被除名。
    /// 框中不跟踪在地板上或下层发送时间的 <see cref="DeliveryResult.Expired"/>,
    /// 包括后时钟跳过。 收件箱从不接受发货, 时间要晚于它的时间加上
    /// 窗口中,它拒绝这样的发送,而当信息被解跟踪且在
    /// 允许进入的楼层。 A grain给自己发的信息使用时钟,或后退后的第一次在地板上方
    /// 因此,每一份处理的记录都有资格在两倍的这一窗口内被删除。
    /// 接收器的时钟,或者在地板之后 当自我发送后会发生后向跳跃。
    /// </para>
    /// <para>
    /// 当信件过期和保留记录的时间长度时,时钟跳转和扭曲会更改;重复后
    /// 日记地板和每个发送发送的固定时间。 此窗口大小以超过 <see cref="OutboxRetryTimeout"/> 截止2013年
    /// 交付延误和时钟改变应用程序的耐受性,并在整个组群中统一配置。
    /// </para>
    /// </remarks>
    /// <value>
    /// 解码窗口。 必须大于 <see cref="OutboxRetryTimeout"/>。 默认为 7 天 。
    /// </value>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 在发送失败的发件箱或处理失败尝试第一次重试之前获取或设置延迟 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次发送失败后,包括目标信箱返回时,发件箱的间隔将加倍
    /// <see cref="DeliveryResult.Backpressured"/>,最多64倍此间隔。在
    /// 处理器失败 。
    /// </para>
    /// <para>
    /// 较短的延迟(e.g.,100米)能够更快地恢复目标信息快速处理时的信号,
    /// 延迟时间更长(e.g.,5秒)
    /// 减少重试管理费用,但增加信息传递的延迟时间。
    /// </para>
    /// <para>
    /// 对于高通量情景来说 后压快速恢复很重要
    /// 考虑值在100-500米之间。对于时间敏感度较低的工作量,1-5秒是适当的。
    /// </para>
    /// </remarks>
    /// <value>
    /// 初始重试间隔。 必须大于零。 默认为 1 秒 。
    /// </value>
    public TimeSpan RetryInitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设定信箱在信件发送前要将处理者试图丢出的信件重试多久
    /// 写得一无所有
    /// </summary>
    /// <remarks>
    /// 超时从第一次失败尝试开始,而上次尝试在它倒塌时进行。处理者例外
    /// 使信息的结果未知,所以回溯横跨瞬断层,例如断线和部署
    /// 修正错误; 处理者立刻调用 <c>IInboxHandlerContext.Fail</c> 来写一个信件, 以破封方式发送。 正在重新尝试信件
    /// 常居其中, <see cref="MaxCapacity"/>工作时间长于可持久的工作时间,
    /// 用于每次重试的觉醒时间。
    /// </remarks>
    /// <value>
    /// 必须大于或等于 <see cref="TimeSpan.Zero"/>,它写了一条信息 在它的第二次失败:
    /// 恢复无法从坠机运行前中断的失败尝试中看出失败的尝试 。
    /// 默认为6小时。
    /// </value>
    public TimeSpan InboxRetryTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 获取或设置在信息发送时间后多久 发件箱试图传送
    /// 信件, 包括接收器被回压或报告没有收件箱处理程序时的邮件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发件箱日记 信息的信息的 <see cref="InboxMessage.SentAt"/> 当它使送文阶段,
    /// 恢复后保持了反压。 超时跟随“ 持久工作钟” 的时钟; 跳跃可以提前结束回溯, 或者
    /// 超时或 <see cref="DeliveryResult.Expired"/> 使接收者早前交付结果
    /// 未知的源 <see cref="IOutboxMessageSource.OnRetired"/> 应用阶段
    /// 留存; 见 <see cref="OutboxMessageRetirement.DeadLettered"/>。
    /// </para>
    /// <para>
    /// 发件箱对每个 <see cref="DeliveryResult.HandlerNotFound"/> 回答错误进行日志。
    /// </para>
    /// </remarks>
    /// <value>
    /// 必须大于零且小于 <see cref="DeduplicationWindow"/>,所以在
    /// 接收器的保留窗口,默认为6小时
    /// </value>
    public TimeSpan OutboxRetryTimeout { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 获取或设置保留信箱和发件箱的死信多长时间。
    /// </summary>
    public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// 获取或设定每个信箱和每个发件箱保留的最大死亡信件数量。
    /// </summary>
    public int MaxRetainedDeadLetters { get; set; } = 1000;

    /// <summary>
    /// 获取或设定通过一个信息泵通行证处理的最多信箱信息数量。
    /// </summary>
    public int InboxBatchSize { get; set; } = 32;

    /// <summary>
    /// 获取或设定一个 grain在飞行中发送发包的最大尝试次数。
    /// </summary>
    /// <remarks>
    /// 泵一旦完成,就启动另一次尝试 所以一个慢速接收器 保持一个位置,没有拖延
    /// 给其他收件人的电文。
    /// </remarks>
    public int OutboxBatchSize { get; set; } = 32;

    /// <summary>
    /// 如果配置值无效, 校验配置值和抛掷 。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 如果 <see cref="MaxCapacity"/> 小于或等于零,则扔弃
    /// 或 <see cref="DeduplicationWindow"/> 小于或等于 <see cref="TimeSpan.Zero"/>,
    /// 或如果重试、保留或批量选项超出其支持范围。
    /// </exception>

    /// <remarks>
    /// 这种方法通常在服务登记期间被依赖性注射容器称为
    /// 以确保配置值在系统启动前有效。
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
