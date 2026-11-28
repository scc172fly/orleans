namespace Orleans.Durable.Journaling;

/// <summary>
/// 目录返回的日志标识及可选元数据快照。
/// </summary>
/// <param name="Id">日志标识。</param>
/// <param name="Metadata">
/// 一起观测到的日志格式、存储 ETag 和完整的调用方拥有的元数据属性；
/// 未请求元数据或无法从列举中取得元数据时为 <see langword="null"/>。
/// </param>
/// <remarks>
/// 元数据描述日志被列举时的情况。使用其 ETag 进行条件更新；
/// 并发变更可能使快照失效。非 null 快照具有与
/// <see cref="IJournalStorage.GetMetadataAsync"/> 相同的元数据语义。
/// </remarks>
public readonly record struct JournalCatalogEntry(JournalId Id, IJournalMetadata? Metadata = null);
