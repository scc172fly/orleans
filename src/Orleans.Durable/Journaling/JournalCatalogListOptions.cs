namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于枚举日志存储标识的选项。
/// </summary>
/// <remarks>
/// 所有约束均采用序数比较。提供程序在枚举开始时对这些选项取快照。
/// </remarks>
public sealed class JournalCatalogListOptions
{
    /// <summary>
    /// 获取或设置 <see cref="JournalId.Value"/> 的原始前缀。默认值匹配所有 ID。
    /// </summary>
    /// <remarks>
    /// 前缀可以结束于路径段内部，例如 <c>jobs/shards/20260909</c>。
    /// 添加末尾斜杠以选择某个命名空间的后代。
    /// </remarks>
    public JournalId Prefix { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="JournalId.Value"/> 的包含端点的下界，使用
    /// <see cref="StringComparison.Ordinal"/> 比较。默认值不施加下界。
    /// </summary>
    public JournalId MinId { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="JournalId.Value"/> 的包含端点的上界，使用
    /// <see cref="StringComparison.Ordinal"/> 比较。默认值不施加上界。
    /// </summary>
    public JournalId MaxId { get; set; }

    /// <summary>
    /// 获取或设置目录是否包含其列举操作可提供的元数据。
    /// 默认为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 提供程序在无需单独为每个日志执行元数据请求且可获得完整元数据快照时，
    /// 投影该快照。否则，<see cref="JournalCatalogEntry.Metadata"/> 为 <see langword="null"/>。
    /// 需要缺失元数据的调用方可使用 <see cref="IJournalStorage.GetMetadataAsync"/> 检索它。
    /// </remarks>
    public bool IncludeMetadata { get; set; }
}
