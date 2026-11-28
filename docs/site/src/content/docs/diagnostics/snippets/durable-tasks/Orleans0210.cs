namespace Documentation.Diagnostics.DurableTasks;

public sealed class StockGrain : DurableGrain
{
#pragma warning disable ORLEANS0210
    // <orleans0210_violation>
    public async DurableTask<bool> ReserveAllAsync(int[] productIds)
    {
        // 每个 Lambda 表达式按输入顺序调度其子任务，每次调用分别进行一次日志写入。
        var reserved = await Task.WhenAll(productIds.Select(async id => await Reserve(id).WithId($"reserve-{id}")));
        return reserved.All(r => r);
    }
    // </orleans0210_violation>
#pragma warning restore ORLEANS0210

    // <orleans0210_fix_when_all>
    public async DurableTask<bool> ReserveEveryLineAsync(int[] productIds)
    {
        // 输入按顺序标识为 reserve/0、reserve/1 等，其调用在一次写入中提交。
        var reserved = await DurableTask.WhenAll(productIds.Select(Reserve)).WithId("reserve");
        return reserved.All(r => r);
    }
    // </orleans0210_fix_when_all>

    // <orleans0210_fix_race>
    public async DurableTask<int> ReserveFromFirstWarehouseAsync(int[] warehouses)
    {
        // 代替 Task.WhenAny：最先响应的仓库获胜，其余请求被取消并等待其结束。
        var (index, _) = await DurableTask.Race(warehouses.Select(Reserve)).WithId("first-warehouse");
        return warehouses[index];
    }
    // </orleans0210_fix_race>

    private static async DurableTask<bool> Reserve(int id) => id > 0;
}
