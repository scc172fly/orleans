using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface IBuyerGrain : IGrainWithStringKey
{
    Task<OrderSummary[]> GetOrdersAsync();

    DurableTask RecordOrderAsync(OrderSummary summary);
}

// 按买家维护的订单状态投影，用于提供买家的订单列表。
// 每个订单的工作流通过分离的持久化调用记录其状态变更。
public sealed class BuyerGrain : DurableGrain, IBuyerGrain
{
    private readonly IDurableDictionary<Guid, OrderSummary> _orders;

    public BuyerGrain()
    {
        _orders = StateManager.GetOrAddDictionary<Guid, OrderSummary>("orders");
    }

    public Task<OrderSummary[]> GetOrdersAsync() =>
        Task.FromResult(_orders.Values.OrderByDescending(order => order.OrderDate).ToArray());

    // 变更与任务结果一同提交。
    public async DurableTask RecordOrderAsync(OrderSummary summary)
    {
        // 订单工作流将每项变更作为分离任务执行，因此变更可能以任意顺序到达；版本号最高的变更是最新的。
        if (!_orders.TryGetValue(summary.OrderId, out var current) || current.Version < summary.Version)
        {
            _orders[summary.OrderId] = summary;
        }
    }
}
