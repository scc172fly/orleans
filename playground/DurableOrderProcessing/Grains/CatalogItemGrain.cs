using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface ICatalogItemGrain : IGrainWithIntegerKey
{
    Task<CatalogItem?> GetAsync();

    DurableTask<bool> ReserveAsync(Guid orderId, int units);

    // 返回该订单在释放前是否持有库存预留，以使释放操作具有幂等性。
    DurableTask<bool> ReleaseAsync(Guid orderId);

    DurableTask CommitAsync(Guid orderId);
}

// 每种商品由一个 Grain 管理其库存。订单在等待验证时预留库存，
// 支付完成后确认扣减，取消时释放库存。每次操作的状态变更与其结果一同提交。
// 库存预留结果独立于持久化任务的保留期，记录每个订单产生的影响。
public sealed class CatalogItemGrain : DurableGrain, ICatalogItemGrain
{
    private readonly IDurableValue<CatalogItem> _item;
    private readonly IDurableDictionary<Guid, int> _reservations;
    private readonly IDurableDictionary<Guid, StockReservation> _reservationOutcomes;

    public CatalogItemGrain()
    {
        _item = StateManager.GetOrAddValue<CatalogItem>("item");
        _reservations = StateManager.GetOrAddDictionary<Guid, int>("reservations");
        _reservationOutcomes = StateManager.GetOrAddDictionary<Guid, StockReservation>("reservation-outcomes");
    }

    private int ProductId => (int)this.GetPrimaryKeyLong();

    // 商品首次发生库存变更后，日志中才会保存该商品；此前使用初始数据条目。
    private CatalogItem? Item => _item.Value ?? CatalogData.Find(ProductId);

    public Task<CatalogItem?> GetAsync() => Task.FromResult(Item);

    public async DurableTask<bool> ReserveAsync(Guid orderId, int units)
    {
        if (_reservationOutcomes.TryGetValue(orderId, out var outcome))
        {
            if (outcome.Units != units)
            {
                throw new InvalidOperationException($"Order {orderId} already requested a different reservation quantity.");
            }

            return outcome.Confirmed;
        }

        var item = Item;
        var confirmed = item is not null && units > 0 && item.AvailableStock >= units;
        if (confirmed)
        {
            _item.Value = item! with
            {
                AvailableStock = item.AvailableStock - units,
                ReservedStock = item.ReservedStock + units,
            };
            _reservations[orderId] = units;
        }

        _reservationOutcomes[orderId] = new(units, confirmed);
        return confirmed;
    }

    public async DurableTask<bool> ReleaseAsync(Guid orderId)
    {
        if (!_reservations.Remove(orderId, out var units))
        {
            return false;
        }

        var item = Item!;
        _item.Value = item with
        {
            AvailableStock = item.AvailableStock + units,
            ReservedStock = item.ReservedStock - units,
        };
        return true;
    }

    public async DurableTask CommitAsync(Guid orderId)
    {
        if (_reservations.Remove(orderId, out var units))
        {
            var item = Item!;
            _item.Value = item with
            {
                ReservedStock = item.ReservedStock - units,
                SoldStock = item.SoldStock + units,
            };
        }
    }
}
