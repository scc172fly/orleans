using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供缓冲日记账数据从存储中读取的存取权限。
/// </summary>
/// <remarks>
/// 事例是集合缓冲页上的临时光标,不得超出提供这些缓冲页的同步调用范围而予以保留。
/// </remarks>
public readonly struct JournalBufferReader
{
    /// <summary>
    /// 初始化 <see cref="JournalBufferReader"/> 结构的新实例。
    /// </summary>
    /// <param name="reader">基本缓冲读取器。</param>
    /// <param name="isCompleted">表示是否不再将数据附加到此缓冲线上的数值。</param>
    public JournalBufferReader(ArcBufferReader reader, bool isCompleted)
    {
        Reader = reader;
        IsCompleted = isCompleted;
    }

    internal readonly ArcBufferReader Reader { get; }

    /// <summary>
    /// 获得一个值,以显示是否不再将数据附加到此缓冲中。
    /// </summary>
    public bool IsCompleted { get; }

    /// <summary>
    /// 获取未读字节数 。
    /// </summary>
    public readonly int Length => Reader.Length;

    /// <summary>
    /// 获得包含下一个 <paramref name="count"/> 字节的宽度, 不读取它们 。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <param name="destination">如果请求的字节不毗连,则使用临时储存。</param>
    /// <returns>包含请求字节的宽度。在消费者返回后,不得保留返回的宽度。</returns>
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
    /// 试图将下一字节复制到 <paramref name="destination"/> , 但没有读取它们 。
    /// </summary>
    /// <param name="destination">复制对象。</param>
    /// <returns><see langword="true"/> ,如果有足够的字节;否则, <see langword="false"/>。</returns>
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
    /// 将剩余字节复制为新数组,而不读取它们。
    /// </summary>
    /// <returns>包含剩余字节的字节数组。</returns>
    public readonly byte[] ToArray()
    {
        var result = new byte[Length];
        Peek(result.Length, result).CopyTo(result);
        return result;
    }

    /// <summary>
    /// a 返回提供长度的切片,不读取数据。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <returns>
    /// 一块未读数据, 电话人拥有返回的缓冲带, 必须将其处理 。
    /// </returns>
    public readonly ArcBuffer Peek(int count) => Reader.PeekSlice(count);

    /// <summary>
    /// 读取字节, 直到找到 <paramref name="delimiter"/> 。
    /// </summary>
    /// <param name="slice">
    /// 如果找到, 则在分隔符前的定点字节。 调用者拥有返回的缓冲, 必须将其处理 。
    /// </param>
    /// <param name="delimiter">用于搜索的分隔符。</param>
    /// <param name="advancePastDelimiter">是否在找到该分隔符时通过该分隔符前进。</param>
    /// <returns><see langword="true"/> 如果找到划界器;否则, <see langword="false"/>。</returns>
    public readonly bool TryReadTo(out ArcBuffer slice, byte delimiter, bool advancePastDelimiter = true) =>
        Reader.TryReadTo(out slice, delimiter, advancePastDelimiter);

    /// <summary>
    /// 检查下个字节是否匹配 <paramref name="next"/>。
    /// </summary>
    /// <param name="next">与下个字节比较的字节。</param>
    /// <param name="advancePast">如果字节匹配, 是否要提前过 。</param>
    /// <returns><see langword="true"/> 如果下一个字节匹配;否则, <see langword="false"/>。</returns>
    public readonly bool IsNext(ReadOnlySpan<byte> next, bool advancePast = false) => Reader.IsNext(next, advancePast);

    /// <summary>
    /// 下一字节的复数将显示为 <paramref name="destination"/> ,并读取它们。
    /// </summary>
    /// <param name="destination">复制对象。</param>
    public readonly void Read(Span<byte> destination) => Reader.Consume(destination);

    /// <summary>
    /// 试图将下一字节复制到 <paramref name="destination"/> 并读取它们 。
    /// </summary>
    /// <param name="destination">复制对象。</param>
    /// <returns><see langword="true"/> ,如果有足够的字节;否则, <see langword="false"/>。</returns>
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
    /// 跳过下个 <paramref name="count"/> 字节。
    /// </summary>
    /// <param name="count">要跳过的字节数。</param>
    public readonly void Skip(int count) => Reader.Skip(count);

    /// <summary>
    /// 试图跳过下个 <paramref name="count"/> 字节 。
    /// </summary>
    /// <param name="count">要跳过的字节数。</param>
    /// <returns><see langword="true"/> ,如果有足够的字节;否则, <see langword="false"/>。</returns>
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
