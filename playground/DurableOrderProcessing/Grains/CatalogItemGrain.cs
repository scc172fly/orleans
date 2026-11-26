using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;

namespace DurableOrderProcessing.Grains;

public interface ICatalogItemGrain : IGrainWithIntegerKey
{
    Task<CatalogItem?> GetAsync();

    DurableTask<bool> ReserveAsync(Guid orderId, int units);

    // 返回命令是否持有保留,因此释放是无效的。
    DurableTask<bool> ReleaseAsync(Guid orderId);

    DurableTask CommitAsync(Guid orderId);
}

// 每产品一个 grain 拥有其库存。
// 支付后, 承诺偿还, 取消时释放。 每一次行动都承诺国家变化 并承诺结果 。
// 保留结果保留每个订单的效力,而不考虑是否保留持久任务。
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

    // 日记在股票首次变化时持有该物品;在此之前,它是种子条目。
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
