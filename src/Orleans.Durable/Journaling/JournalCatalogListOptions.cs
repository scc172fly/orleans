namespace Orleans.Durable.Journaling;

/// <summary>
/// 列出日记存储身份的选项。
/// </summary>
/// <remarks>
/// 提供商在开始查点时对这些选项进行速记。
/// </remarks>
public sealed class JournalCatalogListOptions
{
    /// <summary>
    /// 获取或设置 <see cref="JournalId.Value"/>的原始前缀。默认值符合所有ids。
    /// </summary>
    /// <remarks>
    /// 前缀可以在路径段内结束,例如 <c>jobs/shards/20260909</c>。
    /// 包含一个后遗斜线以选择命名空间的子孙。
    /// </remarks>
    public JournalId Prefix { get; set; }

    /// <summary>
    /// 获取或设置包含式下拉约束 <see cref="JournalId.Value"/>比较使用
    /// <see cref="StringComparison.Ordinal"/>. 默认值不强制设定下限。
    /// </summary>
    public JournalId MinId { get; set; }

    /// <summary>
    /// 将 <see cref="JournalId.Value"/>上的包容性上下约束设或获取
    /// <see cref="StringComparison.Ordinal"/>. 默认值不强制设定上下限。
    /// </summary>
    public JournalId MaxId { get; set; }

    /// <summary>
    /// 获取或确定目录是否包含其列表操作中可用的元数据。
    /// 默认为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果提供商在没有单独每期日刊的情况下提供完整的元数据快报
    /// 请求中的数据。 否则, <see cref="JournalCatalogEntry.Metadata"/> 是 <see langword="null"/>。
    /// 需要缺失元数据的调用者可以通过 <see cref="IJournalStorage.GetMetadataAsync"/>检索。
    /// </remarks>
    public bool IncludeMetadata { get; set; }
}
