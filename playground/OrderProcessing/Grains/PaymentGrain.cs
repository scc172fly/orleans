using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IPaymentGrain : IGrainWithGuidKey;

// 与 eShop 的 PaymentProcessor 类似，为单个订单执行支付授权。
// 尾号为 0002 的银行卡会被拒绝。
public sealed class PaymentGrain : DurableGrain, IPaymentGrain
{
    private readonly IOutbox _outbox;
    private readonly OrderMessages _messages;

    public PaymentGrain(IInbox inbox, IOutbox outbox, OrderMessages messages)
    {
        _outbox = outbox;
        _messages = messages;
        inbox.Register(messages.AuthorizePayment, HandleAuthorizeAsync);
    }

    private async ValueTask HandleAuthorizeAsync(AuthorizePayment request, IInboxHandlerContext context, CancellationToken cancellationToken)
    {
        // 在此处调用实际的支付网关。处理程序至少执行一次，因此应将消息 ID 传递给
        // 网关作为幂等键。
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800)), cancellationToken);
        var result = request.CardLast4 == "0002"
            ? new PaymentResult(false, "The card was declined.")
            : new PaymentResult(true, Reason: null);

        _outbox.SendReply(_messages.PaymentResult, context, request.ReplyTo, result);
        context.Complete();
    }
}
