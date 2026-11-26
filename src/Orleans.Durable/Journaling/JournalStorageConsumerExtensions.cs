using System.Buffers;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 阅读日记存储数据的帮助方法。
/// </summary>
public static class JournalStorageConsumerExtensions
{
    /// <summary>
    /// 通知 <paramref name="consumer"/> 不再提供数据。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    public static void Complete(this IJournalStorageConsumer consumer, IJournalMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        ReadBuffer(consumer, buffer, metadata, isCompleted: true);
    }

    /// <summary>
    /// 用品 <paramref name="input"/> 至 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="input">字节读取。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    /// <param name="complete">是否通知消费者不再提供数据。如果 <see langword="false"/>,则消费者必须读取所有提供的字节。</param>
    public static void Read(this IJournalStorageConsumer consumer, ReadOnlyMemory<byte> input, IJournalMetadata? metadata, bool complete)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        if (!input.IsEmpty)
        {
            buffer.Write(input.Span);
            ReadBuffer(consumer, buffer, metadata, isCompleted: false);
        }

        CompleteOrThrowIfUnread(consumer, buffer, metadata, complete);
    }

    /// <summary>
    /// 用品 <paramref name="input"/> 至 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="input">字节读取。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    /// <param name="complete">是否通知消费者不再提供数据。如果 <see langword="false"/>,则消费者必须读取所有提供的字节。</param>
    public static void Read(this IJournalStorageConsumer consumer, ReadOnlySequence<byte> input, IJournalMetadata? metadata, bool complete)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        foreach (var segment in input)
        {
            if (segment.IsEmpty)
            {
                continue;
            }

            buffer.Write(segment.Span);
            ReadBuffer(consumer, buffer, metadata, isCompleted: false);
        }

        CompleteOrThrowIfUnread(consumer, buffer, metadata, complete);
    }

    /// <summary>
    /// 订购的用品 <paramref name="segments"/> 至 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="segments">命令的字节读取。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    /// <param name="complete">是否通知消费者不再提供数据。如果 <see langword="false"/>,则消费者必须读取所有提供的字节。</param>
    public static void Read(this IJournalStorageConsumer consumer, IEnumerable<ReadOnlyMemory<byte>> segments, IJournalMetadata? metadata, bool complete)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(segments);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        foreach (var segment in segments)
        {
            if (segment.IsEmpty)
            {
                continue;
            }

            buffer.Write(segment.Span);
            ReadBuffer(consumer, buffer, metadata, isCompleted: false);
        }

        CompleteOrThrowIfUnread(consumer, buffer, metadata, complete);
    }

    private static void CompleteOrThrowIfUnread(IJournalStorageConsumer consumer, ArcBufferWriter buffer, IJournalMetadata metadata, bool complete)
    {
        if (complete)
        {
            ReadBuffer(consumer, buffer, metadata, isCompleted: true);
        }

        if (buffer.Length > 0)
        {
            throw new InvalidOperationException("The journal storage consumer did not read all supplied journal data.");
        }
    }

    /// <summary>
    /// 读取 <paramref name="input"/> 的所有字节,并逐步将其供给到 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="input">要读取的流。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>读取的字数 <paramref name="input"/>.</returns>
    public static async ValueTask<long> ReadAsync(this IJournalStorageConsumer consumer, Stream input, IJournalMetadata? metadata, CancellationToken cancellationToken)
        => await consumer.ReadAsync(input, metadata, complete: true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 读取 <paramref name="input"/> 的所有字节,并逐步将其供给到 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日记存储消费者</param>
    /// <param name="input">要读取的流。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    /// <param name="complete">是否通知消费者不再提供数据。如果 <see langword="false"/>,则消费者必须读取所有提供的字节。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>读取的字数 <paramref name="input"/>.</returns>
    public static async ValueTask<long> ReadAsync(this IJournalStorageConsumer consumer, Stream input, IJournalMetadata? metadata, bool complete, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(input);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        long totalBytesRead = 0;
        while (true)
        {
            var memory = buffer.GetMemory();
            var bytesRead = await input.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                CompleteOrThrowIfUnread(consumer, buffer, metadata, complete);
                return totalBytesRead;
            }

            buffer.AdvanceWriter(bytesRead);
            totalBytesRead += bytesRead;

            ReadBuffer(consumer, buffer, metadata, isCompleted: false);
        }
    }

    private static void ReadBuffer(IJournalStorageConsumer consumer, ArcBufferWriter buffer, IJournalMetadata metadata, bool isCompleted)
    {
        var readBuffer = new JournalBufferReader(buffer.Reader, isCompleted);
        consumer.Read(readBuffer, metadata);
    }
}
