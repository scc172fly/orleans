namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置在快照间存取变化不定的日记历史。
/// </summary>
/// <remarks>
/// 供应商和孤立的储存实例在施工期间捕捉了这些价值。
/// 每本日记都计算成功存储附加电话,包括空附件。
/// </remarks>
public sealed class VolatileJournalStorageOptions
{
    /// <summary>
    /// 获取或设置成功附件的数量,然后存储请求快照。
    /// 默认值为100。数值必须是正数 。
    /// </summary>
    public int MaxAppendsBeforeSnapshot { get; set; } = 100;

    /// <summary>
    /// 获取或设置附加的字节数,然后存储要求快照。
    /// 默认值为 1 MiB (1,048, 576 字节)。 数值必须是正数 。
    /// </summary>
    /// <remarks>
    /// 存储请求在达到任一限制时快照。
    /// 编码的日记字节, 排除了快照字节。 成功抓取或删除两个计数台 。
    /// 国家经理在下次写下之前先核对申请,这样一个单独的附件可以超过这一限制。
    /// </remarks>
    public long MaxBytesBeforeSnapshot { get; set; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAppendsBeforeSnapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBytesBeforeSnapshot);
    }
}
