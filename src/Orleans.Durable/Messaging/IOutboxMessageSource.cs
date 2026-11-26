namespace Orleans.Durable.Messaging;

/// <summary>
/// 提供输出信息,其有效载荷和接收程序以其自身日记状态储存。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IOutbox.Send(MessageId)"/> 阶段一个带有电文识别和发送信息的参考条目
/// 状态,但没有有效载荷或接收器, 因此有效载荷会被日记一次, 在应用程序的状态下。 发件箱保存着
/// 普通信息的每一发件语义:发送后发送是耐久的,回溯为后,
/// 字母的编号,在 <c>OutboxRetryTimeout</c>、唤醒和度量后。
/// </para>
/// <para>
/// 发件箱在发送信件时和每次发送尝试前呼叫 <see cref="TryGetMessage"/> 。
/// 返回信件在激活寿命期间拥有的该信件的已注册源;从
/// 日记, 第一个返回信件或扔出信息源的首个来源拥有它。 当条目退出时, 发件箱会拨打
/// 当前激活中该条目的所有人 <see cref="OnRetired"/> 同步, 且在
/// 删除条目。源代码日记的更改和在同一日记笔中执行的清除承诺。如果激活
/// 在那之前的写作失败, 恢复会恢复未完成的条目和来源先前公布的状态,
/// 留存回收条目再次引用 <see cref="OnRetired"/> 。
/// </para>
/// <para>
/// 源代码通过 <see cref="IOutbox.Withdraw"/>撤回待发信件,例如,在应用时间之后
/// 发件箱将取出该有效载荷,然后用同一日记写的方式释放。
/// 输入时不呼叫 <see cref="OnRetired"/> ,不再试图发货。
/// 仍然发送信件并读取 <see cref="TryGetMessage"/> 回复的缓冲, 所以不要改变它们:
/// 使用这些工具,
/// </para>
/// <para>
/// 无法在发送后提供待定信息的来源, 例如因为其存储的有效载荷不再存在
/// 解码, 从 <see cref="TryGetMessage"/>丢弃; 无效信件被同样方式处理。 然后发件箱
/// 写着死亡信,单凭它而入其中,而不交付它,并且召唤 <see cref="OnRetired"/> 与
/// <see cref="OutboxMessageRetirement.DeadLettered"/>; grain的短信和其他邮件
/// 未受影响。 追回的条目,如未登记来源提供的任何注册来源,其用同以死字母方式写成,无业主
/// 。因为没有有效的电文存在,所以该死信的电文仅包含其 <see cref="Envelope.MessageId"/>,
/// 带有空有效载荷和默认 <see cref="OutboxMessage.ReceiverId"/>,其原因记录为失败。
/// </para>
/// <para>
/// 所有电话都在启动调度器上运行。 在激活设置期间, 日记恢复前登记源数: a
/// 恢复的引用项在无源代码被注册时失败激活。当源提供消息时,它会自动
/// 返回每个电话的相同信息、信封和接收器。
/// </para>
/// </remarks>
public interface IOutboxMessageSource
{
    /// <summary>将信息转化为传递或发送未遂的信息。</summary>
    /// <param name="messageId">参考条目的识别号。</param>
    /// <param name="message">
    /// 当此方法返回 <see langword="true"/>时,该方法返回其信封 <see cref="Envelope.MessageId"/>的有效电文
    /// 是 <paramref name="messageId"/> 且别的接受者, grain.
    /// </param>
    /// <returns>
    /// <see langword="true"/> 当该源提供信息时; <see langword="false"/> 当信息不是
    /// 来源于此。
    /// </returns>
    /// <remarks>
    /// 发件箱验证信件。在 <see cref="IOutbox.Send(MessageId)"/>中,有例外或无效
    /// 信件失败, 没有任何文件被显示。 然后, 此条目是可持久的: 例外, 无效的信件,
    /// 包括一个不同 <see cref="Envelope.MessageId"/> 或发送 grain的发件人,或
    /// <see langword="false"/> 来源的源,该条目自该来源,该来源拥有条目,但该条目以失败方式失效
    /// 将失败的实现算作送货尝试,然后发箱
    /// <see cref="OnRetired"/> 与 <see cref="OutboxMessageRetirement.DeadLettered"/>.
    /// </remarks>
    bool TryGetMessage(MessageId messageId, out OutboxMessage message);

    /// <summary>通知源头 发信箱正在运行 信息已过期, 因此源头可以释放其有效载荷。</summary>
    /// <param name="messageId">退役参考条目的编号 。</param>
    /// <param name="retirement">如何取消信息, 和死亡信的原因 写了一条死字的讯息。</param>
    /// <remarks>
    /// <para>
    /// 释放和其他持久效应阶段同步地在日记状态下同步;它们以原子方式与
    /// 删除条目。 此回调运行一次, 在当前激活的条目中输入。 恢复后可以再次援引它
    /// 当退休书写没有被承诺时,从来源的已恢复日记状态开始。
    /// 任何影响都发生在日记之外 写出一等权,不时重复电话
    /// 有关长期适用成果或后续工作的阶段
    /// <see cref="OutboxMessageRetirement.DeadLettered"/> 和
    /// <see cref="OutboxMessageRetirement.Rejected"/>,因此追回保留了申请的答复
    /// 诊断后,经压缩的死信被封。
    /// 利用应用一级的能力来调和这种不确定的结果。
    /// </para>
    /// <para>
    /// 发件箱在拨打此电话之前 复制了一条死信的电文 有效载荷和信头价值
    /// 除非源代码未提供消息, 来源可以在此释放或再使用缓冲。 不要依赖
    /// 此处解码有效载荷时:此呼叫还将信息退去,而源头无法再提供有效载荷。
    /// 启动信息失效, 关闭 grain。
    /// </para>
    /// </remarks>
    void OnRetired(MessageId messageId, OutboxMessageRetirement retirement);
}

/// <summary>描述发件箱消息如何退出。</summary>
/// <remarks>发件箱生成退休; 来源读取它们时用 <see cref="IOutboxMessageSource.OnRetired"/>。</remarks>
public union OutboxMessageRetirement(
    OutboxMessageRetirement.Delivered,
    OutboxMessageRetirement.DeadLettered,
    OutboxMessageRetirement.Rejected)
{
    /// <summary>接收人长期接受电文,或作为重复报告。</summary>
    public sealed class Delivered
    {
        /// <summary>共同退休</summary>
        public static Delivered Instance { get; } = new();
    }

    /// <summary>
    /// 发件箱用破字母写了这个信息:在它的重试限制或它被打破之前,它的发送尝试没有成功。
    /// 源代码无法提供, 或者接收者回答 <see cref="DeliveryResult.Expired"/>。 接收者的
    /// 早交付成果尚不清楚:早先的尝试本可以持久地被接受。
    /// </summary>
    /// <param name="reason">死亡信的原因。</param>
    public sealed class DeadLettered(string reason)
    {
        /// <summary>得到死亡信的原因。</summary>
        public string Reason { get; } = reason;
    }

    /// <summary>接收人以报告的理由和代码拒绝该信息,这可持久地令人感到不解。</summary>
    /// <param name="reason">接收器报告的原因。</param>
    /// <param name="code">接收器报告的应用定义代码。</param>
    public sealed class Rejected(string reason, string? code = null)
    {
        /// <summary>了解接收器报告的原因。</summary>
        public string Reason { get; } = reason;

        /// <summary>
        /// 获取接收器的收件箱信息汇槽拒绝时报告的应用程序定义代码,作为
        /// <see cref="DeliveryResult.DeadLettered.Code"/>; <see langword="null"/> 当时,没有接报者,
        /// </summary>
        public string? Code { get; } = code;
    }
}
