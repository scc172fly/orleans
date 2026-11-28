namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供日志存储实例的目录操作。
/// </summary>
/// <remarks>
/// 目录发现存储标识以及可选的元数据快照。<see cref="IJournalStorage"/> 提供存储生命周期、
/// 元数据及数据变更操作。
/// </remarks>
public interface IJournalStorageCatalog
{
    /// <summary>
    /// 枚举与所提供选项匹配的日志条目。
    /// </summary>
    /// <param name="options">列举选项；使用 <see langword="null"/> 列举不带元数据的所有条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按提供程序遍历顺序排列的匹配条目。</returns>
    /// <remarks>
    /// 枚举开始时对选项取快照。原始前缀以及包含端点的上下界均生效。
    /// 不保证结果已经排序；需要排序的调用方必须对选中的 ID 进行排序。
    /// <see cref="JournalCatalogListOptions.IncludeMetadata"/> 请求从列举中获取可用的完整元数据快照。
    /// 如果提供程序无法投影元数据，或者没有请求元数据，条目携带 <see langword="null"/> 元数据。
    /// 提供程序在内部获取存储分页，并在发现匹配条目时
    /// 逐个返回。推进枚举器可能遍历多个空页或被筛选的存储页。
    /// 存储服务决定请求延迟、重试及内部扫描工作量。
    /// 枚举观察实时存储；并发变更遵循提供程序的列举语义。
    /// 调用方应容忍并发变更期间的重复标识，并开始新的枚举以
    /// 发现之后的变更。需要唯一标识时，按 <see cref="JournalCatalogEntry.Id"/> 去重；
    /// 重复条目可能携带不同的元数据版本。提前停止时释放枚举器。
    /// 存储错误和取消错误通过枚举传播。列举出错后开始新的枚举。
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 已取消。</exception>
    IAsyncEnumerable<JournalCatalogEntry> ListAsync(
        JournalCatalogListOptions? options = null,
        CancellationToken cancellationToken = default);
}
