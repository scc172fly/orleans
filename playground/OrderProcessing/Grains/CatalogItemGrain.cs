using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface ICatalogItemGrain : IGrainWithIntegerKey
{
    Task<CatalogItem?> GetAsync();
}

// 每产品一个 grain 拥有其库存。
// 支付后,应缴还,注销时,应发还。
public sealed class CatalogItemGrain : DurableGrain, ICatalogItemGrain
{
    private readonly IOutbox _outbox;
    private readonly OrderMessages _messages;
    private readonly IDurableValue<CatalogItem> _item;
    private readonly IDurableDictionary<Guid, int> _reservations;
    private readonly IDurableDictionary<Guid, StockReservation> _reservationOutcomes;

    public CatalogItemGrain(IInbox inbox, IOutbox outbox, OrderMessages messages)
    {
        _outbox = outbox;
        _messages = messages;
        _item = StateManager.GetOrAddValue<CatalogItem>("item");
        _reservations = StateManager.GetOrAddDictionary<Guid, int>("reservations");
        _reservationOutcomes = StateManager.GetOrAddDictionary<Guid, StockReservation>("reservation-outcomes");
        inbox.Register(messages.ReserveStock, HandleReserve)
            .Register(messages.ReleaseStock, HandleRelease)
            .Register(messages.CommitStock, HandleCommit);
    }

    private int ProductId => (int)this.GetPrimaryKeyLong();

    // 日记在股票首次变化时持有该物品;在此之前,它是种子条目。
    private CatalogItem? Item => _item.Value ?? CatalogData.Find(ProductId);

    public Task<CatalogItem?> GetAsync() => Task.FromResult(Item);

    // 答复是在股票变动前进行,所以发送的剩余存货未再作重试。
    // 每项订单在释放或实施后保留其结果,而不论信箱重复。
    private void HandleReserve(ReserveStock request, IInboxHandlerContext context)
    {
        if (_reservationOutcomes.TryGetValue(request.OrderId, out var outcome))
        {
            if (outcome.Units is { } units && units != request.Units)
            {
                context.Fail($"Order {request.OrderId} already requested a different reservation quantity.");
                return;
            }

            _outbox.SendReply(_messages.StockResult, context, request.ReplyTo, new StockResult(ProductId, outcome.Confirmed));
            context.Complete();
            return;
        }

        var item = Item;
        var reserved = _reservations.ContainsKey(request.OrderId);
        var reserve = !reserved && item is not null && request.Units > 0 && item.AvailableStock >= request.Units;
        _outbox.SendReply(_messages.StockResult, context, request.ReplyTo, new StockResult(ProductId, reserved || reserve));

        if (reserve)
        {
            _item.Value = item! with
            {
                AvailableStock = item.AvailableStock - request.Units,
                ReservedStock = item.ReservedStock + request.Units,
            };
            _reservations[request.OrderId] = request.Units;
        }

        _reservationOutcomes[request.OrderId] = new(request.Units, reserved || reserve);
        context.Complete();
    }

    private void HandleRelease(ReleaseStock request, IInboxHandlerContext context)
    {
        // 超过储备的释放记录了该命令的终局否决
        _reservationOutcomes.TryAdd(request.OrderId, new(null, false));
        if (_reservations.Remove(request.OrderId, out var units))
        {
            var item = Item!;
            _item.Value = item with
            {
                AvailableStock = item.AvailableStock + units,
                ReservedStock = item.ReservedStock - units,
            };
        }

        context.Complete();
    }

    private void HandleCommit(CommitStock request, IInboxHandlerContext context)
    {
        _reservationOutcomes.TryAdd(request.OrderId, new(null, false));
        if (_reservations.Remove(request.OrderId, out var units))
        {
            var item = Item!;
            _item.Value = item with
            {
                ReservedStock = item.ReservedStock - units,
                SoldStock = item.SoldStock + units,
            };
        }

        context.Complete();
    }
}
