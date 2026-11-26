namespace Orleans.Durable.Journaling;

/// <summary>
/// 从 <see cref="IJournalStorage"/> 实例读取原始日记数据。
/// </summary>
public interface IJournalStorageConsumer
{
    /// <summary>
    /// 读取缓冲原始日记数据。
    /// </summary>
    /// <param name="buffer">消费者可得到的缓冲日记数据。</param>
    /// <param name="metadata">与正在读取的日记数据相关的元数据,或 <see langword="null"/> 如果没有元数据。</param>
    void Read(JournalBufferReader buffer, IJournalMetadata? metadata);
}

/// <summary>
/// 与日记存储相关的元数据。
/// </summary>
public interface IJournalMetadata
{
    /// <summary>
    /// 获取与日记数据存储的日记格式密钥,如果没有密钥,则获取 <see langword="null"/> 。
    /// </summary>
    string? FormatKey { get; }

    /// <summary>
    /// 获取存储元数据ETag,或 <see langword="null"/> ,如果没有。
    /// </summary>
    string? ETag { get; }

    /// <summary>
    /// 获得呼叫者拥有的存储元数据属性。
    /// </summary>
    IReadOnlyDictionary<string, string> Properties { get; }
}

/// <summary>
/// <see cref="IJournalMetadata"/>的默认执行。
/// </summary>
public sealed class JournalMetadata : IJournalMetadata
{
    /// <summary>
    /// 获得空元数据实例 。
    /// </summary>
    public static IJournalMetadata Empty { get; } = new JournalMetadata(formatKey: null, eTag: null, properties: null);

    /// <summary>
    /// 初始化 <see cref="JournalMetadata"/> 类的新实例。
    /// </summary>
    /// <param name="formatKey">与日记数据存储的日记格式密钥,或 <see langword="null"/> 如果不存在密钥, 则使用此密钥 。</param>
    /// <param name="eTag">存储元数据ETag,或 <see langword="null"/> 如果没有。</param>
    /// <param name="properties">调频器拥有的存储元数据属性。</param>
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
