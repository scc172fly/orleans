namespace Orleans.Configuration;

/// <summary>
/// 配置一个命名内存流提供者的缓存。
/// </summary>
public class MemoryStreamCacheOptions
{
    /// <summary>
    /// 获取或设定每次取消操作中要求的队列记录的最大数量 。
    /// </summary>
    /// <remarks>
    /// 值必须大于零。 每记录包含一整批已公布的事件。
    /// 较小值减少被序列成解排响应的记录数量,而较大
    /// 以记录计数, 限制值;
    /// 以字节表示的响应大小取决于这些记录按序排列的大小。
    /// </remarks>
    public int MaxAddCount { get; set; } = DefaultMaxAddCount;

    /// <summary>
    /// 默认值 <see cref="MaxAddCount"/>。
    /// </summary>
    public const int DefaultMaxAddCount = 100;
}
