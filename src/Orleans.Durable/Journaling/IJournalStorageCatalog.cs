namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供日记存储实例的目录操作。
/// </summary>
/// <remarks>
/// 目录发现存储身份和可选元数据快照。 <see cref="IJournalStorage"/> 提供存储生命周期,
/// 元数据,和数据突变操作
/// </remarks>
public interface IJournalStorageCatalog
{
    /// <summary>
    /// 列出符合所提供选项的日记条目。
    /// </summary>
    /// <param name="options">列表选项,或将所有条目无元数据列出的 <see langword="null"/> 。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>供应商跨行顺序中匹配条目。</returns>
    /// <remarks>
    /// 当开始计数时, 选项会被快速地切换。 原始前缀和包容性下限及上界都适用。
    /// 无法保证对结果进行分类;需要订购的呼叫者必须排序选定的代号。
    /// <see cref="JournalCatalogListOptions.IncludeMetadata"/> 要求从清单中获取完整的元数据快照。
    /// 当提供商无法预测或未请求时,条目含有 <see langword="null"/> 元数据。
    /// 供应商内部取回存储网页,并进行收益匹配条目
    /// 推进该数字器可以穿行多个空或过滤的存储页面。
    /// 存储服务确定请求的延迟、回溯和内部扫描工作。
    /// 数字数观察现场储存;同时改动遵循供应商的列表语义。
    /// 调用者应容忍在同时改变时重复身份,并开始重新点查
    /// 发现后发现更改。 当身份独特时, 复制 <see cref="JournalCatalogEntry.Id"/>
    /// 需要;重复输入可以包含不同的元数据版本。在早期停止时将数字解析 。
    /// 存储和注销错误通过点数传播。 在列出错误后开始新的点数 。
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 已被取消。</exception>
    IAsyncEnumerable<JournalCatalogEntry> ListAsync(
        JournalCatalogListOptions? options = null,
        CancellationToken cancellationToken = default);
}
