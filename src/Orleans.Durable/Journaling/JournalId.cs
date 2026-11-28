namespace Orleans.Durable.Journaling;

/// <summary>
/// 独立于任何 Grain 激活实例标识一个日志。
/// </summary>
public readonly struct JournalId : IEquatable<JournalId>
{
    private const char Separator = '/';

    /// <summary>
    /// 初始化 <see cref="JournalId"/> 结构的新实例。
    /// </summary>
    /// <param name="value">稳定日志标识符。</param>
    public JournalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// 获取稳定日志标识符。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 获取一个值，指示此实例是否为默认值。
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// 从 Grain ID 创建日志 ID。
    /// </summary>
    /// <param name="grainId">Grain ID。</param>
    /// <returns>日志 ID。</returns>
    public static JournalId FromGrainId(GrainId grainId)
    {
        if (grainId.IsDefault)
        {
            throw new ArgumentException("The grain id must not be the default value.", nameof(grainId));
        }

        return new(grainId.ToString());
    }

    /// <summary>
    /// 从解码后的层次路径段创建日志 ID。
    /// </summary>
    /// <param name="firstSegment">第一个 ID 段。</param>
    /// <param name="additionalSegments">其他 ID 段。</param>
    /// <returns>规范化的日志 ID。</returns>
    public static JournalId Create(string firstSegment, params ReadOnlySpan<string> additionalSegments)
    {
        var encodedSegments = new string[additionalSegments.Length + 1];
        encodedSegments[0] = EncodeSegment(firstSegment, nameof(firstSegment));
        for (var i = 0; i < additionalSegments.Length; i++)
        {
            encodedSegments[i + 1] = EncodeSegment(additionalSegments[i], nameof(additionalSegments));
        }

        return new(string.Join(Separator, encodedSegments));
    }

    /// <summary>
    /// 从解码后的层次路径段创建日志 ID。
    /// </summary>
    /// <param name="segments">ID 段。</param>
    /// <returns>规范化的日志 ID。</returns>
    public static JournalId Create(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return Create(segments.ToArray().AsSpan());
    }

    /// <summary>
    /// 从解码后的层次路径段创建日志 ID。
    /// </summary>
    /// <param name="segments">ID 段。</param>
    /// <returns>规范化的日志 ID。</returns>
    public static JournalId Create(ReadOnlySpan<string> segments)
    {
        if (segments.Length == 0)
        {
            throw new ArgumentException("A journal id must contain at least one segment.", nameof(segments));
        }

        var encodedSegments = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            encodedSegments[i] = EncodeSegment(segments[i], nameof(segments));
        }

        return new(string.Join(Separator, encodedSegments));
    }

    /// <summary>
    /// 判断此 ID 是否为 <paramref name="journalId"/> 的前缀。
    /// </summary>
    /// <param name="journalId">要测试的日志 ID。</param>
    /// <returns><see langword="true"/> 表示此 ID 为默认值、等于 <paramref name="journalId"/> 或标识其祖先段。</returns>
    public bool IsPrefixOf(JournalId journalId)
    {
        if (IsDefault)
        {
            return true;
        }

        if (journalId.IsDefault)
        {
            return false;
        }

        return string.Equals(journalId.Value, Value, StringComparison.Ordinal)
            || journalId.Value.StartsWith(Value + Separator, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override string ToString() => Value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(JournalId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is JournalId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

    /// <summary>
    /// 比较两个日志 ID 是否相等。
    /// </summary>
    /// <param name="left">第一个日志 ID。</param>
    /// <param name="right">第二个日志 ID。</param>
    /// <returns><see langword="true"/> 表示日志 ID 相等；否则为 <see langword="false"/>。</returns>
    public static bool operator ==(JournalId left, JournalId right) => left.Equals(right);

    /// <summary>
    /// 比较两个日志 ID 是否不相等。
    /// </summary>
    /// <param name="left">第一个日志 ID。</param>
    /// <param name="right">第二个日志 ID。</param>
    /// <returns><see langword="true"/> 表示日志 ID 不相等；否则为 <see langword="false"/>。</returns>
    public static bool operator !=(JournalId left, JournalId right) => !left.Equals(right);

    private static string EncodeSegment(string segment, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment, parameterName);
        if (segment is "." or "..")
        {
            throw new ArgumentException("Journal id segments must not be '.' or '..'.", parameterName);
        }

        if (segment.IndexOf('\0', StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("Journal id segments must not contain null characters.", parameterName);
        }

        return Uri.EscapeDataString(segment);
    }
}
