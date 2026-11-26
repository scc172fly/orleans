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

// 买主的篮子 清盘后分期提交订单
// 替换 eShop的整合事件日志及其交易发件箱。
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

    // 请求的ID 成为命令的ID。 篮子会记住它 CheckoutRetention的结结关, 所以重新审阅
    // 此期间的结结出返回相同的顺序, 即使后来的结结出也返回同样的顺序。 重试写先状态, 所以
    // 其早期写作失败的退步在返回命令编号前一直持续。
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
