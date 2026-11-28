using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface IBasketGrain : IGrainWithStringKey
{
    Task<BasketItem[]> GetAsync();

    Task<BasketItem[]> SetQuantityAsync(int productId, int quantity);

    // 将购物车中的商品移交给订单 orderId，并清空购物车。
    DurableTask<OrderLine[]> CheckoutAsync(Guid orderId);
}

// 买家的购物车。商品数量是直接设置而非递增的，因此重复更新或重试更新具有幂等性。
public sealed class BasketGrain : DurableGrain, IBasketGrain
{
    private readonly IDurableDictionary<int, BasketItem> _items;

    public BasketGrain()
    {
        _items = StateManager.GetOrAddDictionary<int, BasketItem>("items");
    }

    public Task<BasketItem[]> GetAsync() => Task.FromResult(_items.Values.ToArray());

    public async Task<BasketItem[]> SetQuantityAsync(int productId, int quantity)
    {
        if (quantity > 0)
        {
            var product = await GrainFactory.GetGrain<ICatalogItemGrain>(productId).GetAsync()
                ?? throw new ArgumentException($"Product {productId} does not exist.", nameof(productId));
            _items[productId] = new BasketItem(productId, product.Name, product.Price, quantity);
        }
        else
        {
            _items.Remove(productId);
        }

        await WriteStateAsync();
        return _items.Values.ToArray();
    }

    // 订单工作流将此调用作为第一步。清空后的购物车状态与任务结果一同提交，且
    // 运行时针对该订单的任务只执行一次此调用，因此重试或重放结算会返回相同的订单明细。
    public async DurableTask<OrderLine[]> CheckoutAsync(Guid orderId)
    {
        OrderLine[] lines = [.. _items.Values.Select(item => new OrderLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity))];
        _items.Clear();
        return lines;
    }
}
