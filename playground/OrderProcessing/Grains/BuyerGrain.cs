using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IBuyerGrain : IGrainWithStringKey
{
    Task<OrderSummary[]> GetOrdersAsync();
}

// 每位买方都投递订单状况信息,用于买方的订单清单。
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
        // 状态信息可按顺序发送;最高版本为最新版本。
        if (!_orders.TryGetValue(summary.OrderId, out var current) || current.Version < summary.Version)
        {
            _orders[summary.OrderId] = summary;
        }

        context.Complete();
    }
}
