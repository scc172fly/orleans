namespace Documentation.Diagnostics.DurableTasks;

#pragma warning disable ORLEANS0215
// <orleans0215_violation>
public sealed class ExportGrain(ILogger<ExportGrain> logger) : DurableGrain
{
    public async DurableTask<int> ExportAsync(string[] partitions)
    {
        foreach (var partition in partitions)
        {
            // 每次任务重放时, 都再次登录每个已完成分区 。
            logger.LogInformation("Exporting partition {Partition}.", partition);
            await Export(partition).WithId("partition");
        }

        return partitions.Length;
    }

    private static async DurableTask<bool> Export(string partition) => partition.Length > 0;
}
// </orleans0215_violation>
#pragma warning restore ORLEANS0215

// <orleans0215_fix>
public sealed class ReplaySafeExportGrain(ILogger<ReplaySafeExportGrain> logger) : DurableGrain
{
    // 任务重放时跳过事件, 所以每个事件都记录一次。 普通 grain 方法可以使用相同的日志 。
    private readonly ILogger _logger = logger.ReplaySafe();

    public async DurableTask<int> ExportAsync(string[] partitions)
    {
        foreach (var partition in partitions)
        {
            _logger.LogInformation("Exporting partition {Partition}.", partition);
            await Export(partition).WithId("partition");
        }

        return partitions.Length;
    }

    private static async DurableTask<bool> Export(string partition) => partition.Length > 0;
}
// </orleans0215_fix>
