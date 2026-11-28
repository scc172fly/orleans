using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 其他 Grain 通过此 Grain 扩展与当前 Grain 交换持久化消息。
/// </summary>
[Alias("Orleans.Durable.Messaging.IMessagingExtension")]
public interface IMessagingExtension : IGrainExtension
{
    /// <summary>
    /// 将消息投递到此 Grain 的收件箱。
    /// </summary>
    /// <param name="message">接收的命令，必须包含标识和有效载荷；其接收方为此 Grain。</param>
    /// <param name="cancellationToken">取消调用方对投递结果的等待。</param>
    /// <remarks>
    /// 直接准入按照不可变发布契约共享有效载荷数组、标头字典及标头值数组。
    /// RPC 拷贝与反序列化通过普通 Orleans 序列化机制隔离有效载荷数组、标头字典及标头值数组。
    /// 成功结果表示收件箱已持久化接纳消息，或已处理过该消息。
    /// 重试投递的调用方复用消息的 <see cref="InboxMessage.SentAt"/>。
    /// <para>
    /// 投递与 Grain 的其他请求交错执行。投递不会运行处理器：它同步验证、去重，
    /// 并暂存消息接纳操作，随后等待该次日志写入完成，由消息泵稍后运行处理器。
    /// 收件箱消息接收器在投递内部同步运行，因此它与处理器一样，可以在其他请求
    /// 因 await 而挂起时运行。交错执行使并发投递能够共享日志写入，也使消息能够
    /// 到达正在运行请求且该请求等待此消息的 Grain。
    /// </para>
    /// </remarks>
    /// <returns>指示投递或处理状态的结果。</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="message"/> 的消息 ID 未设置、超出标识准入限制，或者有效载荷或标头无效。
    /// 在使用可选标准元数据的契约处对其进行验证。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 消息的 <see cref="InboxMessage.SentAt"/> 晚于接收方时钟加上 <c>DeduplicationWindow</c>，
    /// 且该消息未被跟踪并高于准入下限。收件箱不会为其暂存任何变更。
    /// </exception>
    [Alias("DeliverAsync")]
    [AlwaysInterleave]
    ValueTask<DeliveryResult> DeliverAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
