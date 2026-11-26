using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 管理特定 silo的工件碎片生命周期。
/// </summary>
/// <remarks>
/// 每个 silo 例都有自己的软体管理器。
/// </remarks>
public abstract class JobShardManager
{
    /// <summary>
    /// 获取该管理器关联的 silo 地址。
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
    /// <param name="siloAddress"> silo 地址是此管理器所代表的地址。</param>
    protected JobShardManager(SiloAddress siloAddress)
    {
        SiloAddress = siloAddress;
    }

    /// <summary>
    /// 给这个 silo分配无主的工作。
    /// </summary>
    /// <param name="maxDueTime">碎片考虑的最大到期时间。</param>
    /// <param name="maxNewClaims">
    /// 本次呼吁中孤儿碎片的最大要求数量。
    /// 无限制使用 <see cref="int.MaxValue"/> 。
    /// 此 silo 已经拥有的碎片总是被退回, 不论此限制为何 。
    /// </param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>分配给此 silo的工作碎片列表。</returns>
    public abstract Task<List<IJobShard>> AssignJobShardsAsync(DateTimeOffset maxDueTime, int maxNewClaims, CancellationToken cancellationToken);

    /// <summary>
    /// 创建属于此 silo的新工作碎片。
    /// </summary>
    /// <param name="minDueTime">以此碎片中的工作为最小到期时间。</param>
    /// <param name="maxDueTime">工作在此碎片中的最长到期时间 。</param>
    /// <param name="metadata">碎片的可选元数据。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>新建的求职碎片。</returns>
    public abstract Task<IJobShard> CreateShardAsync(DateTimeOffset minDueTime, DateTimeOffset maxDueTime, IDictionary<string, string> metadata, CancellationToken cancellationToken);

    /// <summary>
    /// 将本 silo拥有的碎片解冻。
    /// </summary>
    /// <param name="shard">拆卸的碎片。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>代表非同步操作的任务 。</returns>
    public abstract Task UnregisterShardAsync(IJobShard shard, CancellationToken cancellationToken);

    internal virtual ValueTask<SiloAddress?> GetShardOwnerAsync(string shardId, CancellationToken cancellationToken) => new((SiloAddress?)null);

    internal virtual ValueTask<bool> IsShardOwnedByLocalSiloAsync(string shardId, CancellationToken cancellationToken) => new(true);
}
