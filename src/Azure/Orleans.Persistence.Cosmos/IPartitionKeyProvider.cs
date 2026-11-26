namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 为提供 grain创建分区密钥。
/// </summary>
[Obsolete("Use IDocumentIdProvider instead.")]
public interface IPartitionKeyProvider
{
    /// <summary>
    /// 为提供 grain创建分区密钥。
    /// </summary>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="grainId"> grain 标识符。</param>
    /// <returns>分区键。</returns>
    ValueTask<string> GetPartitionKey(string grainType, GrainId grainId);
}

#pragma warning disable CS0618 // Type or member is obsolete
internal sealed class DefaultPartitionKeyProvider : IPartitionKeyProvider
{
    public ValueTask<string> GetPartitionKey(string grainType, GrainId grainId) => new(CosmosIdSanitizer.Sanitize(grainType));
}
#pragma warning restore CS0618 // Type or member is obsolete
