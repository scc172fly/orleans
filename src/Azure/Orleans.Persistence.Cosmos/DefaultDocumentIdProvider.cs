using static Orleans.Persistence.Cosmos.CosmosIdSanitizer;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// <see cref="IDocumentIdProvider"/>的默认执行。
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
    /// <param name="options">组合组合选项。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> 是 <see langword="null"/>。
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
    /// 获取指定 grain 文档的代号。
    /// </summary>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="grainId">下 grain id. 数据。</param>
    /// <returns>文档id。</returns>
    public string GetId(string grainType, GrainId grainId) => $"{Sanitize(_options.ServiceId)}{KEY_STRING_SEPARATOR}{Sanitize(grainId.Type.ToString()!)}{SeparatorChar}{Sanitize(grainId.Key.ToString()!)}";

    /// <summary>
    /// 获取指定 grain 文档的宇宙 DB 分区密钥。
    /// </summary>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="grainId">下 grain id. 数据。</param>
    /// <returns>文档分区密钥。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="grainType"/> 是 <see langword="null"/>。
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
