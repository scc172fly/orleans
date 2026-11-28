using static Orleans.Persistence.Cosmos.CosmosIdSanitizer;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// <see cref="IDocumentIdProvider"/> 的默认实现。
/// </summary>
public sealed class DefaultDocumentIdProvider : IDocumentIdProvider
{
    private const string KEY_STRING_SEPARATOR = "__";
    private readonly ClusterOptions _options;
#pragma warning disable CS0618 // Type or member is obsolete
    private readonly IPartitionKeyProvider? _partitionKeyProvider;
#pragma warning restore CS0618 // Type or member is obsolete
    internal bool HasCustomPartitionKeyProvider => _partitionKeyProvider is not null;

    /// <summary>
    /// 初始化 <see cref="DefaultDocumentIdProvider"/> 类的新实例。
    /// </summary>
    /// <param name="options">集群选项。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> 为 <see langword="null"/>。
    /// </exception>
    public DefaultDocumentIdProvider(IOptions<ClusterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

#pragma warning disable CS0618 // Type or member is obsolete
    internal DefaultDocumentIdProvider(IOptions<ClusterOptions> options, IPartitionKeyProvider partitionKeyProvider)
        : this(options)
    {
        _partitionKeyProvider = partitionKeyProvider;
    }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <inheritdoc/>
    public ValueTask<(string DocumentId, string PartitionKey)> GetDocumentIdentifiers(string grainType, GrainId grainId)
    {
        var documentId = GetId(grainType, grainId);
        if (_partitionKeyProvider is null)
        {
            return new((documentId, GetPartitionKey(grainType, grainId)));
        }

        return GetDocumentIdentifiers(documentId, grainType, grainId);
    }

    /// <summary>
    /// 获取指定 Grain 文档的 ID。
    /// </summary>
    /// <param name="grainType">Grain 类型名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <returns>文档 ID。</returns>
    public string GetId(string grainType, GrainId grainId) => $"{Sanitize(_options.ServiceId)}{KEY_STRING_SEPARATOR}{Sanitize(grainId.Type.ToString()!)}{SeparatorChar}{Sanitize(grainId.Key.ToString()!)}";

    /// <summary>
    /// 获取指定 Grain 文档的 Cosmos DB 分区键。
    /// </summary>
    /// <param name="grainType">Grain 类型名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <returns>文档分区键。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="grainType"/> 为 <see langword="null"/>。
    /// </exception>
    public string GetPartitionKey(string grainType, GrainId grainId)
    {
        ArgumentNullException.ThrowIfNull(grainType);

        return Sanitize(grainType);
    }

    private async ValueTask<(string DocumentId, string PartitionKey)> GetDocumentIdentifiers(string documentId, string grainType, GrainId grainId)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var partitionKey = await _partitionKeyProvider!.GetPartitionKey(grainType, grainId);
#pragma warning restore CS0618 // Type or member is obsolete
        return (documentId, partitionKey);
    }
}
