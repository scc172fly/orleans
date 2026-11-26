using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 Redis 日记存储的选项。
/// </summary>
public sealed class RedisJournalStorageOptions
{
    /// <summary>
    /// 默认缩压阈值,以字节表示。
    /// </summary>
    public const long DEFAULT_COMPACTION_THRESHOLD_BYTES = 128L * 1024 * 1024;

    /// <summary>
    /// 获取或设置 Redis 客户端配置。
    /// </summary>
    [RedactRedisConfigurationOptions]
    public ConfigurationOptions? ConfigurationOptions { get; set; }

    /// <summary>
    /// 获取或设置用于创建 Redis 连接多路驱动器的代表。
    /// </summary>
    public Func<RedisJournalStorageOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

    /// <summary>
    /// 获取或设置 silo 生命周期阶段,即储存应初始化的周期。
    /// </summary>
    public int InitStage { get; set; } = ServiceLifecycleStage.RuntimeInitialize;

    /// <summary>
    /// 获取或设置提供者使用的 Redis 密钥前缀。
    /// </summary>
    /// <remarks>
    /// 如果未设定,供应商使用 <c>{ServiceId}/journaling</c>。
    /// </remarks>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// 获取或设置用于将日记 ID 转换为 Redis 密钥名组件的代表。
    /// </summary>
    /// <remarks>
    /// 默认映射保存日记 ID, 允许通过前缀过滤密钥
    /// 用户制图要求元数据读取日记代号。
    /// </remarks>
    public Func<JournalId, string> GetKeyName { get; set; } = DefaultGetKeyName;

    /// <summary>
    /// 获取或设置日记长度, 以字节计, 其中 <see cref="IJournalStorage.IsCompactionRequested"/> 返回 <see langword="true"/>。
    /// </summary>
    public long CompactionThresholdBytes { get; set; } = DEFAULT_COMPACTION_THRESHOLD_BYTES;

    /// <summary>
    /// 在每个回收段中获取或设定向日记消费者提供的字节的最大数量。
    /// </summary>
    public int ReadChunkSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// 创建默认 Redis 连接多路驱动器。
    /// </summary>
    /// <param name="options">下 Redis 日记存储选项。</param>
    /// <returns>下 Redis 连接多路驱动器,并有一个值来显示它是否共享。</returns>
    public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisJournalStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!).ConfigureAwait(false), false);
    }

    internal string GetKeyPrefix(string serviceId)
    {
        var keyPrefix = KeyPrefix ?? $"{serviceId}/journaling";
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        return keyPrefix;
    }

    internal string GetKeyNameForJournal(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var keyName = GetKeyName(journalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        return keyName;
    }

    internal bool UsesDefaultKeyName => GetKeyName == DefaultGetKeyName;

    private static string DefaultGetKeyName(JournalId journalId) => journalId.Value;
}

internal sealed class RedactRedisConfigurationOptionsAttribute : RedactAttribute
{
    public override string Redact(object? value) => value is ConfigurationOptions configuration
        ? configuration.ToString(includePassword: false)
        : base.Redact(value);
}
