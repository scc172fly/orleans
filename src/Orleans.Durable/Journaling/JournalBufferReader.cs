using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供对从存储读取的缓冲日志数据的访问。
/// </summary>
/// <remarks>
/// 实例是池化缓冲区页上的临时游标，不得在提供它们的同步调用结束后继续持有。
/// </remarks>
public readonly struct JournalBufferReader
{
    /// <summary>
    /// 初始化 <see cref="JournalBufferReader"/> 结构的新实例。
    /// </summary>
    /// <param name="reader">底层缓冲区读取器。</param>
    /// <param name="isCompleted">一个值，指示不再有数据追加到此缓冲区。</param>
    public JournalBufferReader(ArcBufferReader reader, bool isCompleted)
    {
        Reader = reader;
        IsCompleted = isCompleted;
    }

    internal readonly ArcBufferReader Reader { get; }

    /// <summary>
    /// 获取一个值，指示不再有数据追加到此缓冲区。
    /// </summary>
    public bool IsCompleted { get; }

    /// <summary>
    /// 获取未读字节数。
    /// </summary>
    public readonly int Length => Reader.Length;

    /// <summary>
    /// 获取包含接下来 <paramref name="count"/> 个字节的 Span，但不消耗这些字节。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <param name="destination">所请求字节不连续时使用的临时存储。</param>
    /// <returns>包含所请求字节的 Span。消费者返回后不得继续持有返回的 Span。</returns>
    public readonly ReadOnlySpan<byte> Peek(int count, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length);
        if (destination.Length < count)
        {
            throw new ArgumentException("The destination span is not large enough.", nameof(destination));
        }

        if (count == 0)
        {
            return [];
        }

        var requested = destination[..count];
        return Reader.Peek(in requested)[..count];
    }

    /// <summary>
    /// 尝试将接下来的字节复制到 <paramref name="destination"/>，但不消耗它们。
    /// </summary>
    /// <param name="destination">复制目标。</param>
    /// <returns><see langword="true"/> 表示有足够的可用字节；否则为 <see langword="false"/>。</returns>
    public readonly bool TryPeek(Span<byte> destination)
    {
        if (destination.Length > Length)
        {
            return false;
        }

        Peek(destination.Length, destination).CopyTo(destination);
        return true;
    }

    /// <summary>
    /// 将剩余字节复制到新数组，但不消耗它们。
    /// </summary>
    /// <returns>包含剩余字节的字节数组。</returns>
    public readonly byte[] ToArray()
    {
        var result = new byte[Length];
        Peek(result.Length, result).CopyTo(result);
        return result;
    }

    /// <summary>
    /// 返回指定长度的固定切片，但不消耗数据。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <returns>
    /// 未读数据的固定切片。调用方拥有返回的缓冲区，并且必须释放它。
    /// </returns>
    public readonly ArcBuffer Peek(int count) => Reader.PeekSlice(count);

    /// <summary>
    /// 读取字节，直到找到 <paramref name="delimiter"/>。
    /// </summary>
    /// <param name="slice">
    /// 如果找到分隔符，则为其之前的固定字节。调用方拥有返回的缓冲区，并且必须释放它。
    /// </param>
    /// <param name="delimiter">要搜索的分隔符。</param>
    /// <param name="advancePastDelimiter">找到分隔符后是否前进到其之后。</param>
    /// <returns><see langword="true"/> 表示已找到分隔符；否则为 <see langword="false"/>。</returns>
    public readonly bool TryReadTo(out ArcBuffer slice, byte delimiter, bool advancePastDelimiter = true) =>
        Reader.TryReadTo(out slice, delimiter, advancePastDelimiter);

    /// <summary>
    /// 检查接下来的字节是否与 <paramref name="next"/> 匹配。
    /// </summary>
    /// <param name="next">用于与接下来字节进行比较的字节。</param>
    /// <param name="advancePast">匹配时是否前进到这些字节之后。</param>
    /// <returns><see langword="true"/> 表示接下来的字节匹配；否则为 <see langword="false"/>。</returns>
    public readonly bool IsNext(ReadOnlySpan<byte> next, bool advancePast = false) => Reader.IsNext(next, advancePast);

    /// <summary>
    /// 将接下来的字节复制到 <paramref name="destination"/> 并消耗它们。
    /// </summary>
    /// <param name="destination">复制目标。</param>
    public readonly void Read(Span<byte> destination) => Reader.Consume(destination);

    /// <summary>
    /// 尝试将接下来的字节复制到 <paramref name="destination"/> 并消耗它们。
    /// </summary>
    /// <param name="destination">复制目标。</param>
    /// <returns><see langword="true"/> 表示有足够的可用字节；否则为 <see langword="false"/>。</returns>
    public readonly bool TryRead(Span<byte> destination)
    {
        if (!TryPeek(destination))
        {
            return false;
        }

        Skip(destination.Length);
        return true;
    }

    /// <summary>
    /// 跳过接下来的 <paramref name="count"/> 个字节。
    /// </summary>
    /// <param name="count">要跳过的字节数。</param>
    public readonly void Skip(int count) => Reader.Skip(count);

    /// <summary>
    /// 尝试跳过接下来的 <paramref name="count"/> 个字节。
    /// </summary>
    /// <param name="count">要跳过的字节数。</param>
    /// <returns><see langword="true"/> 表示有足够的可用字节；否则为 <see langword="false"/>。</returns>
    public readonly bool TrySkip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Length)
        {
            return false;
        }

        Skip(count);
        return true;
    }

}
