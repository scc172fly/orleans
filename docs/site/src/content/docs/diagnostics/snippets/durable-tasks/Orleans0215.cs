namespace Documentation.Diagnostics.DurableTasks;

#pragma warning disable ORLEANS0215
// <orleans0215_violation>
public sealed class ExportGrain(ILogger<ExportGrain> logger) : DurableGrain
{
    public async DurableTask<int> ExportAsync(string[] partitions)
    {
        foreach (var partition in partitions)
        {
            // 每次任务重放时，都为每个已完成的分区再次记录日志。
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
    // 任务重放期间跳过日志事件，因此每个事件仅记录一次。普通 Grain 方法也可使用相同的日志记录器。
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
