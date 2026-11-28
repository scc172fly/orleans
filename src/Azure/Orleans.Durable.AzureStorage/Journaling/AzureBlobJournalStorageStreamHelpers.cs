using System.Buffers;

namespace Orleans.Durable.Journaling;

internal static class AzureBlobJournalStorageStreamHelpers
{
    // SkipStreamAsync 将每次不可寻址流的排空读取限制为此大小，而不使用可能更大的池化数组。
    private const int MaxSkipBufferBytes = 16 * 1024;

    internal static async ValueTask SkipStreamAsync(Stream input, long length, CancellationToken cancellationToken)
    {
        // 在恢复期间用于消耗检查点已覆盖的 WAL 字节，而不将它们暴露给使用方。
        if (length <= 0)
        {
            return;
        }

        if (input.CanSeek)
        {
            // 仅在流支持时使用定位操作；Azure 响应流通常只能向前读取。
            input.Seek(length, SeekOrigin.Current);
            return;
        }

        // 不可寻址流只能通过读取向前移动，因此将要丢弃的字节读入池化缓冲区。
        var maxChunkSize = (int)Math.Min(MaxSkipBufferBytes, length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxChunkSize);

        try
        {
            while (length > 0)
            {
                // ArrayPool 可能返回更大的数组，因此使用切片限制每次跳过读取的大小。
                var toRead = (int)Math.Min(maxChunkSize, length);

                // ReadExactlyAsync 保证返回前完全填充缓冲区切片，
                // 若流提前结束则抛出 EndOfStreamException。
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
