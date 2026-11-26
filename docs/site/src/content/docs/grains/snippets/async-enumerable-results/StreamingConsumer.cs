using Orleans.Runtime;

namespace Orleans.Docs.Snippets.AsyncEnumerableResults;

public static class StreamingConsumer
{
    // <consume_stream>
    public static async Task ConsumeStream(IExportGrain grain)
    {
        await foreach (var row in grain.ExportRows())
        {
            await ProcessRow(row);
        }
    }
    // </consume_stream>

    // <configure_batch_size>
    public static async Task ConsumeInBatches(IExportGrain grain)
    {
        await foreach (var row in grain.ExportRows().WithBatchSize(25))
        {
            await ProcessRow(row);
        }
    }
    // </configure_batch_size>

    // <cancel_stream>
    public static async Task ConsumeWithCancellation(
        IExportGrain grain,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var row in grain
                .ExportRows(cancellationToken)
                .WithBatchSize(25)
                .WithCancellation(cancellationToken))
            {
                await ProcessRow(row);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 呼叫者请求取消 。
        }
    }
    // </cancel_stream>

    // <handle_interruption>
    public static async Task ConsumeWithInterruptionHandling(
        IExportGrain grain)
    {
        try
        {
            await foreach (var row in grain.ExportRows())
            {
                await ProcessRow(row);
            }
        }
        catch (EnumerationAbortedException)
        {
            // grain 已停止使用或 Orleans 清除了闲置的分子。
        }
    }
    // </handle_interruption>

    private static Task ProcessRow(ExportRow row) => Task.CompletedTask;
}
