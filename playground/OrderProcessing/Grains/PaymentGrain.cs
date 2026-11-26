using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IPaymentGrain : IGrainWithGuidKey;

// 授权支付一项订单,如 eShop的 PaymentProcessor。
// 以千分之二结束的卡片号码被下调。
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
        // 真正的网关呼叫在这里。 处理器至少运行一次, 所以将消息的 ID传送到
        // 作为其应急密钥的端口。
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800)), cancellationToken);
        var result = request.CardLast4 == "0002"
            ? new PaymentResult(false, "The card was declined.")
            : new PaymentResult(true, Reason: null);

        _outbox.SendReply(_messages.PaymentResult, context, request.ReplyTo, result);
        context.Complete();
    }
}
