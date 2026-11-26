using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface IBuyerGrain : IGrainWithStringKey
{
    Task<OrderSummary[]> GetOrdersAsync();

    DurableTask RecordOrderAsync(OrderSummary summary);
}

// 买方订购单清单的每个买方订单预测状况。
// 每个订单的工作流程记录其状况变化,有独立的耐用电话。
public sealed class BuyerGrain : DurableGrain, IBuyerGrain
{
    private readonly IDurableDictionary<Guid, OrderSummary> _orders;

    public BuyerGrain()
    {
        _orders = StateManager.GetOrAddDictionary<Guid, OrderSummary>("orders");
    }

    public Task<OrderSummary[]> GetOrdersAsync() =>
        Task.FromResult(_orders.Values.OrderByDescending(order => order.OrderDate).ToArray());

    // 变革承诺与任务结果一致。
    public async DurableTask RecordOrderAsync(OrderSummary summary)
    {
        // 顺序的工作流程每次更改都会脱钩, 因此更改会依任何顺序出现; 最高版本是最新版本 。
        if (!_orders.TryGetValue(summary.OrderId, out var current) || current.Version < summary.Version)
        {
            _orders[summary.OrderId] = summary;
        }
    }
}
