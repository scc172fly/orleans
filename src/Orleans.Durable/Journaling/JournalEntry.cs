namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示一条已持久化的日志条目。
/// </summary>
/// <remarks>
/// <para>
/// 读取器由 <see cref="FormatKey"/> 标识的日志格式拥有。此类型
/// 不解释条目数据，也不了解任何特定物理格式。
/// </para>
/// <para>
/// 读取器只保证在提供此条目的同步重放调用期间
/// 保持有效。需要保留数据的实现必须复制数据。
/// </para>
/// </remarks>
/// <param name="formatKey"><paramref name="reader"/> 的日志格式键。</param>
/// <param name="reader">条目有效载荷读取器。</param>
public readonly ref struct JournalEntry(string formatKey, JournalBufferReader reader)
{
    /// <summary>
    /// 获取此条目的日志格式键。
    /// </summary>
    public string FormatKey { get; } = JournalFormatServices.ValidateJournalFormatKey(formatKey);

    /// <summary>
    /// 获取条目有效载荷读取器。
    /// </summary>
    public JournalBufferReader Reader { get; } = reader;
}
