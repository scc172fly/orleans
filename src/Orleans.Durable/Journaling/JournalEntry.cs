namespace Orleans.Durable.Journaling;

/// <summary>
/// 代表一个持续存在的日记账条目。
/// </summary>
/// <remarks>
/// <para>
/// 读者由 <see cref="FormatKey"/>所指定的日记格式拥有。这种类型确实
/// 输入数据未解释,也不了解任何具体的物理格式。
/// </para>
/// <para>
/// 只有在同步重放呼叫期间,读者才能保证保持有效
/// 提供此条目。 需要保存数据的执行必须复制该数据 。
/// </para>
/// </remarks>
/// <param name="formatKey"> <paramref name="reader"/>的日记格式密钥。</param>
/// <param name="reader">进入有效载荷阅读器。</param>
public readonly ref struct JournalEntry(string formatKey, JournalBufferReader reader)
{
    /// <summary>
    /// 获取此条目的日记格式密钥。
    /// </summary>
    public string FormatKey { get; } = JournalFormatServices.ValidateJournalFormatKey(formatKey);

    /// <summary>
    /// 获得进入有效载荷读取器。
    /// </summary>
    public JournalBufferReader Reader { get; } = reader;
}
