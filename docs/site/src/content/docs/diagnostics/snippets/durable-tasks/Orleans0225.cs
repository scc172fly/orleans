namespace Documentation.Diagnostics.DurableTasks;

public sealed class DatasetGrain : DurableGrain
{
#pragma warning disable ORLEANS0225
    // <orleans0225_violation>
    public async DurableTask<string> PublishAsync(string dataset)
    {
        var export = await Export(dataset).WithId("export").ScheduleAsync();

        // 民意测验将 NotSupportedException 投到一个持久的任务机构。
        while (!await export.IsCompletedAsync())
        {
            await DurableTask.Delay(TimeSpan.FromMinutes(1)).WithId("poll-interval");
        }

        return await export;
    }
    // </orleans0225_violation>
#pragma warning restore ORLEANS0225

    // <orleans0225_fix_await>
    public async DurableTask<string> PublishWhenExportedAsync(string dataset)
    {
        // 当导出完成后, 身体会恢复, 重放返回其记录的结果 。
        return await Export(dataset).WithId("export");
    }
    // </orleans0225_fix_await>

    // <orleans0225_fix_timeout>
    public async DurableTask<string?> PublishWithinAsync(string dataset)
    {
        try
        {
            // 持久拖延是出口的竞赛,执行记录也是先完成。
            return await Export(dataset).WaitAsync(TimeSpan.FromHours(1)).WithId("export");
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
    // </orleans0225_fix_timeout>

    private static async DurableTask<string> Export(string dataset) => $"exports/{dataset}";
}
