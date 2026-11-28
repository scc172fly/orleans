using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Orleans.Configuration;

/// <summary>
/// Redis 流选项。
/// </summary>
public sealed class RedisStreamingOptions
{
    /// <summary>
    /// 获取或设置 Redis 客户端选项。
    /// </summary>
    [RedactRedisConfigurationOptionsAttribute]
    public ConfigurationOptions ConfigurationOptions { get; set; } = new();

    /// <summary>
    /// 用于创建 Redis 连接多路复用器并指明其是否共享的委托。
    /// </summary>
    public Func<RedisStreamingOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

    /// <summary>
    /// 条目过期时间，默认为 null。仅应在临时环境（例如测试）中设置此值。
    /// 将其设置为非 null 值会导致流条目及相关检查点键在配置的时长后被删除。
    /// </summary>
    public TimeSpan? EntryExpiry { get; set; } = null;

    /// <summary>
    /// 获取或设置检查点持久化尝试之间的时间间隔。
    /// </summary>
    public TimeSpan CheckpointPersistInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获取或设置应保留的最大流条目数量。
    /// 为 null 时，Redis 流长度不受限制。
    /// </summary>
    public long? MaxStreamLength { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示配置 <see cref="MaxStreamLength"/> 时 Redis 是否应使用近似裁剪。
    /// </summary>
    public bool UseApproximateMaxLength { get; set; } = true;

    /// <summary>
    /// 默认连接多路复用器创建委托。
    /// </summary>
    /// <param name="options">包含 Redis 连接配置的流选项。</param>
    /// <returns>包含已创建的连接多路复用器以及该复用器由提供程序拥有的标志的任务。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
    public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisStreamingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions), false);
    }
}

internal sealed class RedactRedisConfigurationOptionsAttribute : RedactAttribute
{
    public override string Redact(object? value) => value is ConfigurationOptions cfg ? cfg.ToString(includePassword: false) : base.Redact(value);
}
