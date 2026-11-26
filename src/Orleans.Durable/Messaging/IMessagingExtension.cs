using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 下 grain 其它 grains 以此交换耐用信息 grain.
/// </summary>
[Alias("Orleans.Durable.Messaging.IMessagingExtension")]
public interface IMessagingExtension : IGrainExtension
{
    /// <summary>
    /// 向此 grain的收件箱发送消息 。
    /// </summary>
    /// <param name="message">收到的命令具有所要求的身份和有效载荷;其接收器是 grain。</param>
    /// <param name="cancellationToken">取消呼叫者等待交货的时间。</param>
    /// <remarks>
    /// 直接接收共享有效载荷阵列、页眉字典和根据不可改变的出版合同的标头值阵列。
    /// RPC 复制和取消序列化,使用普通 Orleans 序号分离有效载荷阵列、标题词典和页头值阵列。
    /// 成功的结果意味着信箱可以持久地接受信息,或者已经处理过。
    /// 调用器重新尝试发送, 重复使用信件的 <see cref="InboxMessage.SentAt"/>。
    /// <para>
    /// 交付货物与 grain的其他请求发生间断。交付货物没有操作者:它能验证、能分解、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理、能处理
    /// 同步控制信件的接收, 然后等待日记写作, 并且发送信息泵运行
    /// 处理器稍后再处理。 信箱信息汇槽在发送时同步运行, 这样它就可以在请求被发送时运行
    /// 处理人可以等待暂停等候。 插入让同时交付的分期日记写, 并让
    /// 正在运行的请求会等待信件的 grain 。
    /// </para>
    /// </remarks>
    /// <returns>显示交付/处理状况的结果。</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="message"/> 具有未设置的电文代号,超过身份识别允许限,或有效载荷或信头无效。
    /// 其合同消耗的任择标准元数据得到验证。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 信件的 <see cref="InboxMessage.SentAt"/> 晚于接收器的时钟加上 <c>DeduplicationWindow</c>,
    /// 信息没有被跟踪,位于录取层以上。信箱没有为此进行任何步骤。
    /// </exception>
    [Alias("DeliverAsync")]
    [AlwaysInterleave]
    ValueTask<DeliveryResult> DeliverAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
