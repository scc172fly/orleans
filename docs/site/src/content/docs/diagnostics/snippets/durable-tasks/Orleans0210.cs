namespace Documentation.Diagnostics.DurableTasks;

public sealed class StockGrain : DurableGrain
{
#pragma warning disable ORLEANS0210
    // <orleans0210_violation>
    public async DurableTask<bool> ReserveAllAsync(int[] productIds)
    {
        // 每个羊羔按输入顺序安排孩子的日程,为每次祈祷单写日记。
        var reserved = await Task.WhenAll(productIds.Select(async id => await Reserve(id).WithId($"reserve-{id}")));
        return reserved.All(r => r);
    }
    // </orleans0210_violation>
#pragma warning restore ORLEANS0210

    // <orleans0210_fix_when_all>
    public async DurableTask<bool> ReserveEveryLineAsync(int[] productIds)
    {
        // 投入是储备/0,储备/1等,按投入顺序排列,其工作以一个书面形式进行。
        var reserved = await DurableTask.WhenAll(productIds.Select(Reserve)).WithId("reserve");
        return reserved.All(r => r);
    }
    // </orleans0210_fix_when_all>

    // <orleans0210_fix_race>
    public async DurableTask<int> ReserveFromFirstWarehouseAsync(int[] warehouses)
    {
        // 代替 Task.WhenAny:第一个回答的仓库获胜,其他仓库则被取消并等待。
        var (index, _) = await DurableTask.Race(warehouses.Select(Reserve)).WithId("first-warehouse");
        return warehouses[index];
    }
    // </orleans0210_fix_race>

    private static async DurableTask<bool> Reserve(int id) => id > 0;
}
