namespace Orleans.Durable.Journaling;

/// <summary>
/// 从 <see cref="IJournalStorage"/> 实例读取原始日志数据。
/// </summary>
public interface IJournalStorageConsumer
{
    /// <summary>
    /// 读取缓冲的原始日志数据。
    /// </summary>
    /// <param name="buffer">可供消费者使用的缓冲日志数据。</param>
    /// <param name="metadata">与正在读取的日志数据关联的元数据；如果没有元数据，则为 <see langword="null"/>。</param>
    void Read(JournalBufferReader buffer, IJournalMetadata? metadata);
}

/// <summary>
/// 与日志存储关联的元数据。
/// </summary>
public interface IJournalMetadata
{
    /// <summary>
    /// 获取随日志数据存储的日志格式键；不存在该键时为 <see langword="null"/>。
    /// </summary>
    string? FormatKey { get; }

    /// <summary>
    /// 获取存储元数据 ETag；不可用时为 <see langword="null"/>。
    /// </summary>
    string? ETag { get; }

    /// <summary>
    /// 获取调用方拥有的存储元数据属性。
    /// </summary>
    IReadOnlyDictionary<string, string> Properties { get; }
}

/// <summary>
/// <see cref="IJournalMetadata"/> 的默认实现。
/// </summary>
public sealed class JournalMetadata : IJournalMetadata
{
    /// <summary>
    /// 获取空元数据实例。
    /// </summary>
    public static IJournalMetadata Empty { get; } = new JournalMetadata(formatKey: null, eTag: null, properties: null);

    /// <summary>
    /// 初始化 <see cref="JournalMetadata"/> 类的新实例。
    /// </summary>
    /// <param name="formatKey">随日志数据存储的日志格式键；不存在该键时为 <see langword="null"/>。</param>
    /// <param name="eTag">存储元数据 ETag；不可用时为 <see langword="null"/>。</param>
    /// <param name="properties">调用方拥有的存储元数据属性。</param>
    public JournalMetadata(string? formatKey, string? eTag = null, IReadOnlyDictionary<string, string>? properties = null)
    {
        FormatKey = formatKey;
        ETag = eTag;
        Properties = CopyProperties(properties);
    }

    /// <inheritdoc/>
    public string? FormatKey { get; }

    /// <inheritdoc/>
    public string? ETag { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Properties { get; }

    internal static Dictionary<string, string> CopyProperties(IReadOnlyDictionary<string, string>? properties)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties is null)
        {
            return result;
        }

        foreach (var (key, value) in properties)
        {
            ValidateCallerProperty(key, value);
            result.Add(key, value);
        }

        return result;
    }

    internal static void ValidatePropertyName(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (propertyName.IndexOf('\0', StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("Journal metadata property names must not contain null characters.", nameof(propertyName));
        }
    }

    internal static void ValidateCallerPropertyName(string propertyName)
    {
        ValidatePropertyName(propertyName);
        if (IsProviderOwned(propertyName))
        {
            throw new ArgumentException(
                $"Journal metadata property '{propertyName}' is provider-owned. Caller updates must not set or remove provider-owned properties.",
                nameof(propertyName));
        }
    }

    internal static void ValidateCallerProperty(string propertyName, string value)
    {
        ValidateCallerPropertyName(propertyName);
        ArgumentNullException.ThrowIfNull(value);
    }

    internal static bool IsProviderOwned(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return propertyName.StartsWith("$", StringComparison.Ordinal);
    }
}
