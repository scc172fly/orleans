using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface IBasketGrain : IGrainWithStringKey
{
    Task<BasketItem[]> GetAsync();

    Task<BasketItem[]> SetQuantityAsync(int productId, int quantity);

    Task<Guid?> CheckoutAsync(CheckoutRequest request);
}

// 买家的购物车。结算在一次日志写入中清空购物车并暂存订单提交消息，
// 以替代 eShop 的集成事件日志和事务性发件箱。
public sealed class BasketGrain : DurableGrain, IBasketGrain
{
    private static readonly TimeSpan CheckoutRetention = TimeSpan.FromDays(7);

    private readonly IOutbox _outbox;
    private readonly OrderMessages _messages;
    private readonly TimeProvider _timeProvider;
    private readonly TimeProvider _jobClock;
    private readonly IDurableDictionary<int, BasketItem> _items;
    private readonly IDurableDictionary<Guid, DateTimeOffset> _checkouts;

    public BasketGrain(
        IOutbox outbox,
        OrderMessages messages,
        TimeProvider timeProvider,
        [FromKeyedServices(DurableJobTimeProviderNames.DurableJobs)] TimeProvider jobClock)
    {
        _outbox = outbox;
        _messages = messages;
        _timeProvider = timeProvider;
        _jobClock = jobClock;
        _items = StateManager.GetOrAddDictionary<int, BasketItem>("items");
        _checkouts = StateManager.GetOrAddDictionary<Guid, DateTimeOffset>("checkouts");
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

    // 请求 ID 成为订单 ID。购物车在 CheckoutRetention 指定的期限内保留结算记录，因此
    // 在此期限内重试结算会返回相同订单，即使此后已有其他结算。重试会先写入状态，从而确保
    // 先前写入失败的结算在返回其订单 ID 之前完成持久化。
    public async Task<Guid?> CheckoutAsync(CheckoutRequest request)
    {
        if (_checkouts.ContainsKey(request.RequestId))
        {
            await WriteStateAsync();
            return request.RequestId;
        }

        if (_items.Count == 0)
        {
            return null;
        }

        var orderId = request.RequestId;
        var lines = _items.Values
            .Select(item => new OrderLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity))
            .ToArray();
        var now = _timeProvider.GetUtcNow();
        _outbox.Send(
            _messages.SubmitOrder,
            MessageId.Create("orders", orderId.ToString("N"), "submit"),
            GrainFactory.GetGrain<IOrderGrain>(orderId),
            new SubmitOrder(this.GetPrimaryKeyString(), lines, request.Address, request.CardNumber[^4..], _jobClock.GetUtcNow()));
        _items.Clear();
        foreach (var id in _checkouts.Where(pair => now - pair.Value >= CheckoutRetention).Select(pair => pair.Key).ToArray())
        {
            _checkouts.Remove(id);
        }

        _checkouts[orderId] = now;
        await WriteStateAsync();
        return orderId;
    }
}
