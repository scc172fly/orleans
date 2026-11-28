namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 获取 Grain 状态文档的文档标识符和分区标识符。
/// </summary>
public interface IDocumentIdProvider
{
    /// <summary>
    /// 获取指定 Grain 的文档标识符。
    /// </summary>
    /// <param name="grainType">Grain 类型名称。</param>
    /// <param name="grainId">Grain 标识符。</param>
    /// <returns>文档 ID 和分区键。</returns>
    ValueTask<(string DocumentId, string PartitionKey)> GetDocumentIdentifiers(string grainType, GrainId grainId);
}
