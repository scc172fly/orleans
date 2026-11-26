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
        // 重新开发的发明可以在最初的成功反应失败后返回409。
        // 只有公认的成功才能确立所有权;每次失败都需要检查。
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
