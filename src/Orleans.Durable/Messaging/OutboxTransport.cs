using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>为一次发件箱投递尝试调用接收方的收件箱。</summary>
/// <remarks>测试通过替换传输来注入投递故障。</remarks>
internal interface IOutboxTransport
{
    /// <summary>将 <paramref name="message"/> 投递到 <paramref name="receiver"/> 的收件箱。</summary>
    ValueTask<DeliveryResult> DeliverAsync(GrainId receiver, InboxMessage message, CancellationToken cancellationToken);
}

/// <summary>通过对接收方消息传递扩展的 Grain 调用投递发件箱消息。</summary>
internal sealed class GrainOutboxTransport(IGrainFactory grainFactory) : IOutboxTransport
{
    public ValueTask<DeliveryResult> DeliverAsync(GrainId receiver, InboxMessage message, CancellationToken cancellationToken) =>
        grainFactory.GetGrain<IMessagingExtension>(receiver).DeliverAsync(message, cancellationToken);
}
