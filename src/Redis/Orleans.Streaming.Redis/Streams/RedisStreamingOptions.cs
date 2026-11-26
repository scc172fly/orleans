using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Orleans.Configuration;

/// <summary>
/// Redis 流式选项。
/// </summary>
public sealed class RedisStreamingOptions
{
    /// <summary>
    /// 获取或设置 Redis 客户端选项。
    /// </summary>
    [RedactRedisConfigurationOptionsAttribute]
    public ConfigurationOptions ConfigurationOptions { get; set; } = new();

    /// <summary>
    /// 该代表用来创建 Redis 连接多路驱动器,并表明该多路转换器是否共享。
    /// </summary>
    public Func<RedisStreamingOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

    /// <summary>
    /// 输入过期, 默认为无效。 值应该只为短时间环境设定( 如测试中) 。
    /// 设置一个与 null 不同的值, 将导致在配置时间之后删除流条目和相关检查站密钥 。
    /// </summary>
    public TimeSpan? EntryExpiry { get; set; } = null;

    /// <summary>
    /// 获取或设定检查站坚持努力之间的间隔。
    /// </summary>
    public TimeSpan CheckpointPersistInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获取或设定要保留的最大串列数 。
    /// 无效时, Redis 流长将不约束。
    /// </summary>
    public long? MaxStreamLength { get; set; }

    /// <summary>
    /// 获取或设置值以显示 Redis 使用近似三密计算时 <see cref="MaxStreamLength"/> 被配置。
    /// </summary>
    public bool UseApproximateMaxLength { get; set; } = true;

    /// <summary>
    /// 默认的多路驱动器创建代表 。
    /// </summary>
    /// <param name="options">包含 Redis 连接配置的流动选项。</param>
    /// <returns>含有创建的多路驱动器的任务,并注明供应商拥有该多路驱动器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 是 <see langword="null"/>.</exception>
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
