using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 管理指定 Silo 的作业分片生命周期。
/// </summary>
/// <remarks>
/// 每个 Silo 实例均拥有自己的分片管理器。
/// </remarks>
public abstract class JobShardManager
{
    /// <summary>
    /// 获取此管理器关联的 Silo 地址。
    /// </summary>
    protected SiloAddress SiloAddress { get; }

    internal virtual async IAsyncEnumerable<IJobShard> DiscoverJobShardsAsync(
        DateTimeOffset maxDueTime,
        int maxNewClaims,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var shard in await AssignJobShardsAsync(maxDueTime, maxNewClaims, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return shard;
        }
    }

    /// <summary>
    /// 初始化 <see cref="JobShardManager"/> 类的新实例。
    /// </summary>
    /// <param name="siloAddress">此管理器所代表的 Silo 地址。</param>
    protected JobShardManager(SiloAddress siloAddress)
    {
        SiloAddress = siloAddress;
    }

    /// <summary>
    /// 将无主作业分片分配给此 Silo。
    /// </summary>
    /// <param name="maxDueTime">要考虑的分片的最大到期时间。</param>
    /// <param name="maxNewClaims">
    /// 本次调用允许认领的无主分片的最大数量。
    /// 使用 <see cref="int.MaxValue"/> 表示不限制数量。
    /// 无论是否达到此限制，始终返回此 Silo 已拥有的分片。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配给此 Silo 的作业分片列表。</returns>
    public abstract Task<List<IJobShard>> AssignJobShardsAsync(DateTimeOffset maxDueTime, int maxNewClaims, CancellationToken cancellationToken);

    /// <summary>
    /// 创建由此 Silo 拥有的新作业分片。
    /// </summary>
    /// <param name="minDueTime">此分片中作业的最小到期时间。</param>
    /// <param name="maxDueTime">此分片中作业的最大到期时间。</param>
    /// <param name="metadata">分片的可选元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新创建的作业分片。</returns>
    public abstract Task<IJobShard> CreateShardAsync(DateTimeOffset minDueTime, DateTimeOffset maxDueTime, IDictionary<string, string> metadata, CancellationToken cancellationToken);

    /// <summary>
    /// 注销此 Silo 拥有的分片。
    /// </summary>
    /// <param name="shard">要注销的分片。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public abstract Task UnregisterShardAsync(IJobShard shard, CancellationToken cancellationToken);

    internal virtual ValueTask<SiloAddress?> GetShardOwnerAsync(string shardId, CancellationToken cancellationToken) => new((SiloAddress?)null);

    internal virtual ValueTask<bool> IsShardOwnedByLocalSiloAsync(string shardId, CancellationToken cancellationToken) => new(true);
}
