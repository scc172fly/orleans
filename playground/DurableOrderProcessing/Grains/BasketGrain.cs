using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface IBasketGrain : IGrainWithStringKey
{
    Task<BasketItem[]> GetAsync();

    Task<BasketItem[]> SetQuantityAsync(int productId, int quantity);

    // 把篮子物品交给订单 orderId 并清除篮子。
    DurableTask<OrderLine[]> CheckoutAsync(Guid orderId);
}

// 买主的篮子。数量是固定的,而不是加量, 所以重复或重新研究更新是无用之物。
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

    // 命令的工作流程将此称为第一步。
    // 运行时会为命令的任务一次运行调用, 所以重审或重放取出返回相同的行 。
    public async DurableTask<OrderLine[]> CheckoutAsync(Guid orderId)
    {
        OrderLine[] lines = [.. _items.Values.Select(item => new OrderLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity))];
        _items.Clear();
        return lines;
    }
}
