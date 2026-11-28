using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IBuyerGrain : IGrainWithStringKey
{
    Task<OrderSummary[]> GetOrdersAsync();
}

// 按买家维护的订单状态消息投影，用于提供买家的订单列表。
public sealed class BuyerGrain : DurableGrain, IBuyerGrain
{
    private readonly IDurableDictionary<Guid, OrderSummary> _orders;

    public BuyerGrain(IInbox inbox, OrderMessages messages)
    {
        _orders = StateManager.GetOrAddDictionary<Guid, OrderSummary>("orders");
        inbox.Register(messages.StatusChanged, HandleStatusChanged);
    }

    public Task<OrderSummary[]> GetOrdersAsync() =>
        Task.FromResult(_orders.Values.OrderByDescending(order => order.OrderDate).ToArray());

    private void HandleStatusChanged(OrderSummary summary, IInboxHandlerContext context)
    {
        // 状态消息可能乱序到达；版本号最高的消息是最新的。
        if (!_orders.TryGetValue(summary.OrderId, out var current) || current.Version < summary.Version)
        {
            _orders[summary.OrderId] = summary;
        }

        context.Complete();
    }
}
