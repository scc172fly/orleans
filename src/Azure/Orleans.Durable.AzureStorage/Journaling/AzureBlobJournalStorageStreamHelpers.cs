using System.Buffers;

namespace Orleans.Durable.Journaling;

internal static class AzureBlobJournalStorageStreamHelpers
{
    // SkipStreamAsync 将每一不可探测排水管的上限设在以这种尺寸而不是使用较大的集合数组来读取时。
    private const int MaxSkipBufferBytes = 16 * 1024;

    internal static async ValueTask SkipStreamAsync(Stream input, long length, CancellationToken cancellationToken)
    {
        // 回收时用于消费检查站覆盖的WAL字节,而没有将它们暴露在消费者面前。
        if (length <= 0)
        {
            return;
        }

        if (input.CanSeek)
        {
            // 搜索仅在支持时使用; Azure 响应流通常只前方。
            input.Seek(length, SeekOrigin.Current);
            return;
        }

        // 无法寻找的流流只能通过阅读才能向前推进,所以将丢弃的字节排入集合缓冲。
        var maxChunkSize = (int)Math.Min(MaxSkipBufferBytes, length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxChunkSize);

        try
        {
            while (length > 0)
            {
                // ArrayPool 可以返回更大的数组, 所以切开它来保持每次跳过读盖 。
                var toRead = (int)Math.Min(maxChunkSize, length);

                // ReadExactlyAsync 保证缓冲切片在返回前完全填满,
                // 或抛掷一, EndOfStreamException 如果溪流提前结束的话。
                await input.ReadExactlyAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);

                length -= toRead;
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidOperationException("Azure Blob journal WAL ended before the checkpoint offset was reached.", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
