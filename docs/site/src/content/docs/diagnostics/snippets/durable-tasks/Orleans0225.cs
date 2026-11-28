namespace Documentation.Diagnostics.DurableTasks;

public sealed class DatasetGrain : DurableGrain
{
#pragma warning disable ORLEANS0225
    // <orleans0225_violation>
    public async DurableTask<string> PublishAsync(string dataset)
    {
        var export = await Export(dataset).WithId("export").ScheduleAsync();

        // 在持久化任务体中执行轮询会抛出 NotSupportedException。
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
        // 导出完成后任务体恢复执行，重放返回已记录的结果。
        return await Export(dataset).WithId("export");
    }
    // </orleans0225_fix_await>

    // <orleans0225_fix_timeout>
    public async DurableTask<string?> PublishWithinAsync(string dataset)
    {
        try
        {
            // 持久化延迟与导出操作竞争，执行实例记录哪一方先完成。
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
