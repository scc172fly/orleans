namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 为提供的 Grain 创建分区键。
/// </summary>
[Obsolete("Use IDocumentIdProvider instead.")]
public interface IPartitionKeyProvider
{
    /// <summary>
    /// 为提供的 Grain 创建分区键。
    /// </summary>
    /// <param name="grainType">Grain 类型名称。</param>
    /// <param name="grainId">Grain 标识符。</param>
    /// <returns>分区键。</returns>
    ValueTask<string> GetPartitionKey(string grainType, GrainId grainId);
}

#pragma warning disable CS0618 // Type or member is obsolete
internal sealed class DefaultPartitionKeyProvider : IPartitionKeyProvider
{
    public ValueTask<string> GetPartitionKey(string grainType, GrainId grainId) => new(CosmosIdSanitizer.Sanitize(grainType));
}
#pragma warning restore CS0618 // Type or member is obsolete
