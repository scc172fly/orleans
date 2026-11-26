namespace Orleans.Durable.Jobs;

/// <summary>
/// 检查选定“ 持久工作” 日记提供方的完整软化命名空间。
/// </summary>
public interface IDurableJobsStorageInspector
{
    /// <summary>
    /// 计数现有的碎片,包括未来日期和有毒碎片,而不改变所有权。
    /// </summary>
    /// <remarks>
    /// 结果通过供应商目录语义学观察现场储存。
    /// 要求所有调度时间 silos 在空库存之前完成写提供者切换
    /// 它可以证明它的碎片已经耗尽。 存储和取消失败会让操作发生故障 。
    /// </remarks>
    /// <param name="providerName">所选的写入或排出中的日记帐簿提供者名称。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>供应商库存。</returns>
    ValueTask<DurableJobsStorageStatus> InspectAsync(string providerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 由一位选定的《持久工作》日记提供商完成的全目录观测。
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

    /// <summary>获取日记账提供者的名称。</summary>
    public string ProviderName { get; }

    /// <summary>获取此 silo 是否使用提供者创建新碎片。</summary>
    public bool IsWriteProvider { get; }

    /// <summary>获取在软化名称空间中的现有日记数。</summary>
    public long ShardCount { get; }

    /// <summary>获得与有记录的所有权人一起的识别碎片数。</summary>
    public long OwnedShardCount { get; }

    /// <summary>获得识别的有毒碎片数量。</summary>
    public long PoisonedShardCount { get; }

    /// <summary>获取无法解释其硬元数据的日记数量。</summary>
    public long UnrecognizedShardCount { get; }

    /// <summary>获得最早识别的碎片启动时间,或者在不存在时获取 <see langword="null"/> 。</summary>
    public DateTimeOffset? OldestShardStartTime { get; }

    /// <summary>获取最新的识别碎片启动时间,或者没有 <see langword="null"/> 的</summary>。
    public DateTimeOffset? NewestShardStartTime { get; }
}
