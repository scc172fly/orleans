using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// Redis 日志存储配置选项。
/// </summary>
public sealed class RedisJournalStorageOptions
{
    /// <summary>
    /// 默认压实阈值，以字节为单位。
    /// </summary>
    public const long DEFAULT_COMPACTION_THRESHOLD_BYTES = 128L * 1024 * 1024;

    /// <summary>
    /// 获取或设置 Redis 客户端配置。
    /// </summary>
    [RedactRedisConfigurationOptions]
    public ConfigurationOptions? ConfigurationOptions { get; set; }

    /// <summary>
    /// 获取或设置用于创建 Redis 连接多路复用器的委托。
    /// </summary>
    public Func<RedisJournalStorageOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

    /// <summary>
    /// 获取或设置应初始化存储的 Silo 生命周期阶段。
    /// </summary>
    public int InitStage { get; set; } = ServiceLifecycleStage.RuntimeInitialize;

    /// <summary>
    /// 获取或设置提供程序使用的 Redis 键前缀。
    /// </summary>
    /// <remarks>
    /// 如果未设置，提供程序使用 <c>{ServiceId}/journaling</c>。
    /// </remarks>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// 获取或设置用于将日志 ID 转换为 Redis 键名称组成部分的委托。
    /// </summary>
    /// <remarks>
    /// 默认映射保留日志 ID，使发现过程能够按前缀筛选键，
    /// 并在不读取元数据的情况下恢复 ID。自定义映射需要读取元数据才能恢复日志 ID。
    /// </remarks>
    public Func<JournalId, string> GetKeyName { get; set; } = DefaultGetKeyName;

    /// <summary>
    /// 获取或设置使 <see cref="IJournalStorage.IsCompactionRequested"/> 返回 <see langword="true"/> 的日志长度（以字节为单位）。
    /// </summary>
    public long CompactionThresholdBytes { get; set; } = DEFAULT_COMPACTION_THRESHOLD_BYTES;

    /// <summary>
    /// 获取或设置每个恢复分段向日志使用者提供的最大字节数。
    /// </summary>
    public int ReadChunkSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// 创建默认 Redis 连接多路复用器。
    /// </summary>
    /// <param name="options">Redis 日志存储选项。</param>
    /// <returns>Redis 连接多路复用器及其是否共享的标志。</returns>
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
