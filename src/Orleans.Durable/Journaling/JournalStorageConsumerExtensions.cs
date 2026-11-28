using System.Buffers;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于读取日志存储数据的辅助方法。
/// </summary>
public static class JournalStorageConsumerExtensions
{
    /// <summary>
    /// 通知 <paramref name="consumer"/> 不会再提供数据。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    public static void Complete(this IJournalStorageConsumer consumer, IJournalMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        metadata ??= JournalMetadata.Empty;

        using var buffer = new ArcBufferWriter();
        ReadBuffer(consumer, buffer, metadata, isCompleted: true);
    }

    /// <summary>
    /// 将 <paramref name="input"/> 提供给 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="input">要读取的字节。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    /// <param name="complete">是否通知消费者不会再提供数据。如果为 <see langword="false"/>，消费者必须读取全部已提供的字节。</param>
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
    /// 将 <paramref name="input"/> 提供给 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="input">要读取的字节。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    /// <param name="complete">是否通知消费者不会再提供数据。如果为 <see langword="false"/>，消费者必须读取全部已提供的字节。</param>
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
    /// 将有序的 <paramref name="segments"/> 提供给 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="segments">要读取的有序字节。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    /// <param name="complete">是否通知消费者不会再提供数据。如果为 <see langword="false"/>，消费者必须读取全部已提供的字节。</param>
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
    /// 从 <paramref name="input"/> 读取所有字节，并将它们增量提供给 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="input">要读取的流。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>从 <paramref name="input"/> 读取的字节数。</returns>
    public static async ValueTask<long> ReadAsync(this IJournalStorageConsumer consumer, Stream input, IJournalMetadata? metadata, CancellationToken cancellationToken)
        => await consumer.ReadAsync(input, metadata, complete: true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 从 <paramref name="input"/> 读取所有字节，并将它们增量提供给 <paramref name="consumer"/>。
    /// </summary>
    /// <param name="consumer">日志存储消费者。</param>
    /// <param name="input">要读取的流。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    /// <param name="complete">是否通知消费者不会再提供数据。如果为 <see langword="false"/>，消费者必须读取全部已提供的字节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>从 <paramref name="input"/> 读取的字节数。</returns>
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
