namespace Benchmarks.Journaling.Azure;

internal sealed class OwnedBenchmarkResource(
    AzureJournalReport report,
    Func<CancellationToken, Task> create,
    Func<CancellationToken, Task> delete)
{
    private bool _owned;

    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        report.Ownership = "creation-unconfirmed";
        report.Cleanup = "manual-check-required";
        // 首次创建成功的响应丢失后，重试创建可能返回 409。
        // 只有收到成功确认才能确立资源所有权；每次失败都需要检查。
        await create(cancellationToken);

        _owned = true;
        report.Ownership = "owned";
        report.Cleanup = "pending";
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (_owned)
        {
            report.Cleanup = "deleting";
            await delete(cancellationToken);
            _owned = false;
            report.Cleanup = "deleted";
        }
    }
}
