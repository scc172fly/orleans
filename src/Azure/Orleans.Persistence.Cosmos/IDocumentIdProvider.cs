namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 获取 grain 状态文档的文档和分区标识符。
/// </summary>
public interface IDocumentIdProvider
{
    /// <summary>
    /// 获取指定 grain的文档标识符。
    /// </summary>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="grainId"> grain 标识符。</param>
    /// <returns>文档代号和分区键。</returns>
    ValueTask<(string DocumentId, string PartitionKey)> GetDocumentIdentifiers(string grainType, GrainId grainId);
}
