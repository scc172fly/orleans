using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>呼叫一个发件箱发送尝试的接收器收件箱。</summary>
/// <remarks>试验取代了输射投射故障的运输。</remarks>
internal interface IOutboxTransport
{
    /// <summary>交付交付 <paramref name="message"/> 框中 <paramref name="receiver"/>.</summary>
    ValueTask<DeliveryResult> DeliverAsync(GrainId receiver, InboxMessage message, CancellationToken cancellationToken);
}

/// <summary>通过 grain 呼叫接收器的通讯扩展</summary>
internal sealed class GrainOutboxTransport(IGrainFactory grainFactory) : IOutboxTransport
{
    public ValueTask<DeliveryResult> DeliverAsync(GrainId receiver, InboxMessage message, CancellationToken cancellationToken) =>
        grainFactory.GetGrain<IMessagingExtension>(receiver).DeliverAsync(message, cancellationToken);
}
