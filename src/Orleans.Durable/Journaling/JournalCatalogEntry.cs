namespace Orleans.Durable.Journaling;

/// <summary>
/// 一份期刊身份和一份由目录退回的可选元数据快照。
/// </summary>
/// <param name="Id">日记身份。</param>
/// <param name="Metadata">
/// 日记的格式、存储ETag和完全由呼叫者拥有的元数据属性一起观测,
/// 或 <see langword="null"/> ,当没有请求元数据或清单中无法提供时。
/// </param>
/// <remarks>
/// 元数据在列出时描述日记。 使用它的ETag进行有条件更新;
/// 自动同步的截图具有相同的元数据语义
/// 年为 <see cref="IJournalStorage.GetMetadataAsync"/>.
/// </remarks>
public readonly record struct JournalCatalogEntry(JournalId Id, IJournalMetadata? Metadata = null);
