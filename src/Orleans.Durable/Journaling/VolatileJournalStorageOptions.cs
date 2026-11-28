namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置易失性存储在两次快照之间保留的日志历史。
/// </summary>
/// <remarks>
/// 提供程序及隔离存储实例在构造期间捕获这些值。
/// 每个日志对成功的存储追加调用计数，包括空追加。
/// </remarks>
public sealed class VolatileJournalStorageOptions
{
    /// <summary>
    /// 获取或设置存储请求快照之前的成功追加次数。
    /// 默认为 100。该值必须为正数。
    /// </summary>
    public int MaxAppendsBeforeSnapshot { get; set; } = 100;

    /// <summary>
    /// 获取或设置存储请求快照之前的累计追加字节数。
    /// 默认为 1 MiB（1,048,576 字节）。该值必须为正数。
    /// </summary>
    /// <remarks>
    /// 达到任一限制时，存储请求快照。追加字节数度量
    /// 已编码的日志字节数，不包含快照字节。快照或删除成功会重置两个计数器。
    /// 状态管理器在下一次写入前检查请求，因此单次追加可能超出此限制。
    /// </remarks>
    public long MaxBytesBeforeSnapshot { get; set; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAppendsBeforeSnapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBytesBeforeSnapshot);
    }
}
