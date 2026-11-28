namespace Orleans.Durable.Jobs;

/// <summary>
/// 检查选定持久化作业日志提供程序的完整分片命名空间。
/// </summary>
public interface IDurableJobsStorageInspector
{
    /// <summary>
    /// 对现有分片进行计数，包括未来到期的分片和有毒分片，不改变所有权。
    /// </summary>
    /// <remarks>
    /// 结果根据提供程序的目录语义观察实时存储。停用提供程序之前，
    /// 所有负责调度的 Silo 必须完成写入提供程序的切换，只有在此之后，空清单
    /// 才能证明其分片已全部处理完毕。存储或取消失败会使该操作以失败结束。
    /// </remarks>
    /// <param name="providerName">选定的写入或排空中的日志提供程序名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提供程序的清单。</returns>
    ValueTask<DurableJobsStorageStatus> InspectAsync(string providerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 对选定持久化作业日志提供程序的完整目录观察结果。
/// </summary>
public sealed class DurableJobsStorageStatus
{
    internal DurableJobsStorageStatus(
        string providerName,
        bool isWriteProvider,
        long shardCount,
        long ownedShardCount,
        long poisonedShardCount,
        long unrecognizedShardCount,
        DateTimeOffset? oldestShardStartTime,
        DateTimeOffset? newestShardStartTime)
    {
        ProviderName = providerName;
        IsWriteProvider = isWriteProvider;
        ShardCount = shardCount;
        OwnedShardCount = ownedShardCount;
        PoisonedShardCount = poisonedShardCount;
        UnrecognizedShardCount = unrecognizedShardCount;
        OldestShardStartTime = oldestShardStartTime;
        NewestShardStartTime = newestShardStartTime;
    }

    /// <summary>获取日志提供程序的名称。</summary>
    public string ProviderName { get; }

    /// <summary>获取此 Silo 是否使用该提供程序创建新分片。</summary>
    public bool IsWriteProvider { get; }

    /// <summary>获取分片命名空间中现有日志的数量。</summary>
    public long ShardCount { get; }

    /// <summary>获取已识别且记录了所有者的分片数量。</summary>
    public long OwnedShardCount { get; }

    /// <summary>获取已识别的有毒分片数量。</summary>
    public long PoisonedShardCount { get; }

    /// <summary>获取分片元数据无法解释的日志数量。</summary>
    public long UnrecognizedShardCount { get; }

    /// <summary>获取已识别分片的最早开始时间；如果不存在，则为 <see langword="null"/>。</summary>
    public DateTimeOffset? OldestShardStartTime { get; }

    /// <summary>获取已识别分片的最晚开始时间；如果不存在，则为 <see langword="null"/>。</summary>
    public DateTimeOffset? NewestShardStartTime { get; }
}
