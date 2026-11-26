namespace Orleans.Durable.Journaling;

/// <summary>
/// 指定一个日记,独立于任何 grain 激活。
/// </summary>
public readonly struct JournalId : IEquatable<JournalId>
{
    private const char Separator = '/';

    /// <summary>
    /// 初始化 <see cref="JournalId"/> 结构的新实例。
    /// </summary>
    /// <param name="value">稳定的日记账识别符号。</param>
    public JournalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// 获取稳定日记账识别码。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 获得一个值,以显示此实例是否为默认值。
    /// </summary>
    public bool IsDefault => Value is null;

    /// <summary>
    /// 从 grain id 创建日记代号。
    /// </summary>
    /// <param name="grainId">下 grain id. 数据。</param>
    /// <returns>日记id。</returns>
    public static JournalId FromGrainId(GrainId grainId)
    {
        if (grainId.IsDefault)
        {
            throw new ArgumentException("The grain id must not be the default value.", nameof(grainId));
        }

        return new(grainId.ToString());
    }

    /// <summary>
    /// 创建来自解码的等级段的日记代号 。
    /// </summary>
    /// <param name="firstSegment">首个id段</param>
    /// <param name="additionalSegments">额外的代号段。</param>
    /// <returns>正常日记识别码</returns>
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
    /// 创建来自解码的等级段的日记代号 。
    /// </summary>
    /// <param name="segments">ID段。</param>
    /// <returns>正常日记识别码</returns>
    public static JournalId Create(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return Create(segments.ToArray().AsSpan());
    }

    /// <summary>
    /// 创建来自解码的等级段的日记代号 。
    /// </summary>
    /// <param name="segments">ID段。</param>
    /// <returns>正常日记识别码</returns>
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
    /// 确定此id 是否为 <paramref name="journalId"/>前缀。
    /// </summary>
    /// <param name="journalId">要测试的日记 ID。</param>
    /// <returns><see langword="true"/> 如果该代号是默认值, 等于 <paramref name="journalId"/>, 或指向一个祖先段。</returns>
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
    /// 比较了两个杂志的平等标识。
    /// </summary>
    /// <param name="left">第一份日记id。</param>
    /// <param name="right">第二日记id。</param>
    /// <returns><see langword="true"/> 如果日记id是相等的,否则 <see langword="false"/>.</returns>
    public static bool operator ==(JournalId left, JournalId right) => left.Equals(right);

    /// <summary>
    /// 比较了两个期刊不平等的识别码。
    /// </summary>
    /// <param name="left">第一份日记id。</param>
    /// <param name="right">第二日记id。</param>
    /// <returns><see langword="true"/> 如果日记id不相等,否则 <see langword="false"/>.</returns>
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
