using System.Collections.Immutable;

namespace Documentation.Diagnostics.DurableTasks;

#pragma warning disable ORLEANS0223
// <orleans0223_violation>
[GenerateSerializer]
public sealed class Wishlist
{
    [Id(0)]
    public List<string> Items { get; set; } = [];
}

public sealed class WishlistGrain : DurableGrain
{
    private readonly IDurableValue<Wishlist> _wishlist;

    public WishlistGrain() => _wishlist = StateManager.GetOrAddValue<Wishlist>("wishlist");

    public async DurableTask AddAsync(string item)
    {
        await CheckStock(item).WithId("check-stock");

        // 更改值持有的列表:没有日记记录会保留新项目。
        _wishlist.Value!.Items.Add(item);
    }

    private static async DurableTask<bool> CheckStock(string item) => item.Length > 0;
}
// </orleans0223_violation>
#pragma warning restore ORLEANS0223

// <orleans0223_fix>
[GenerateSerializer, Immutable]
public sealed record SavedItems(ImmutableList<string> Items)
{
    public static SavedItems Empty { get; } = new([]);
}

public sealed class SavedItemsGrain : DurableGrain
{
    private readonly IDurableValue<SavedItems> _saved;

    public SavedItemsGrain() => _saved = StateManager.GetOrAddValue<SavedItems>("saved-items");

    public async DurableTask AddAsync(string item)
    {
        await CheckStock(item).WithId("check-stock");

        // 任务通过记录新清单的值 API。
        var saved = _saved.Value ?? SavedItems.Empty;
        _saved.Value = saved with { Items = saved.Items.Add(item) };
    }

    private static async DurableTask<bool> CheckStock(string item) => item.Length > 0;
}
// </orleans0223_fix>
