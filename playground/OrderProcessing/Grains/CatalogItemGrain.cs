using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;

namespace OrderProcessing.Grains;

public interface ICatalogItemGrain : IGrainWithIntegerKey
{
    Task<CatalogItem?> GetAsync();
}

// 每种商品由一个 Grain 管理其库存。订单在等待验证时预留库存，
// 支付后确认扣减库存，取消时释放库存。
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

    // 商品首次发生库存变更后，日志中才会保存该商品；此前使用初始数据条目。
    private CatalogItem? Item => _item.Value ?? CatalogData.Find(ProductId);

    public Task<CatalogItem?> GetAsync() => Task.FromResult(Item);

    // 在库存发生变更前暂存回复，因此发送失败时库存保持不变，可供后续重试。
    // 在释放或确认库存后仍保留每个订单的结果，独立于收件箱去重机制。
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
        // 如果释放请求先于预留请求到达，则记录该订单的终态拒绝结果。
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
