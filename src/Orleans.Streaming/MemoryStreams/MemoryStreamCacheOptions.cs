namespace Orleans.Configuration;

/// <summary>
/// 配置命名的内存流提供程序的缓存。
/// </summary>
public class MemoryStreamCacheOptions
{
    /// <summary>
    /// 获取或设置每次出队操作请求的最大队列记录数。
    /// </summary>
    /// <remarks>
    /// 此值必须大于零。每条记录包含一个已发布的事件批次。
    /// 较小的值会减少序列化到出队响应中的记录数，而较大的值
    /// 可以将队列调用开销分摊到更多记录上。此限制以记录数计量；
    /// 响应的字节大小取决于这些记录序列化后的大小。
    /// </remarks>
    public int MaxAddCount { get; set; } = DefaultMaxAddCount;

    /// <summary>
    /// <see cref="MaxAddCount"/> 的默认值。
    /// </summary>
    public const int DefaultMaxAddCount = 100;
}
