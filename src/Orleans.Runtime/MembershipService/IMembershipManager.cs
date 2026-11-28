#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 管理本地 Silo 的集群成员资格状态及生命周期。
/// 这是成员资格管理的主要抽象，支持
/// 被动（基于表）及主动（基于共识）实现。
/// </summary>
/// <remarks>
/// <para>
/// 实现必须参与 Silo 生命周期和健康检查。
/// 这与 <see cref="IClusterHealthMonitor"/> 遵循相同模式。
/// </para>
/// </remarks>
internal interface IMembershipManager : ILifecycleParticipant<ISiloLifecycle>, IHealthCheckParticipant
{
    /// <summary>
    /// 获取当前成员资格快照。
    /// </summary>
    MembershipTableSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// 获取成员资格更新流。
    /// 成员资格视图发生变化时发布更新。
    /// 使用方应按顺序处理更新。
    /// </summary>
    IAsyncEnumerable<MembershipTableSnapshot> MembershipUpdates { get; }

    /// <summary>
    /// 获取本地 Silo 的当前状态。
    /// </summary>
    SiloStatus LocalSiloStatus { get; }

    /// <summary>
    /// 更新本地 Silo 在集群中的状态。
    /// </summary>
    /// <param name="status">新状态。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>在状态更新完成时完成的任务。</returns>
    /// <exception cref="OrleansException">更新失败时抛出。</exception>
    Task UpdateLocalStatus(SiloStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// 请求宣告某个 Silo 死亡。
    /// 对于被动系统，这触发投票/宣告死亡流程。
    /// 对于主动系统，这可能触发视图变更提议。
    /// </summary>
    /// <param name="silo">要宣告死亡的 Silo。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>Silo 被宣告死亡时为 true，否则为 false。</returns>
    Task<bool> TryKillSilo(SiloAddress silo, CancellationToken cancellationToken);

    /// <summary>
    /// 请求怀疑某个 Silo（可能最终宣告死亡）。
    /// 对于被动系统，这会添加一票。
    /// 对于主动系统，这可能不执行任何操作（由故障检测器处理）。
    /// </summary>
    /// <param name="silo">要怀疑的 Silo。</param>
    /// <param name="indirectProbingSilo">也检测到该故障的 Silo，或者 <see langword="null"/>。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>采取了行动时为 true。</returns>
    Task<bool> TrySuspectSilo(SiloAddress silo, SiloAddress? indirectProbingSilo, CancellationToken cancellationToken);

    /// <summary>
    /// 从权威数据源刷新成员资格视图。
    /// 对于被动系统，这会读取成员表。
    /// 对于主动系统，这可能不执行任何操作，或请求最新视图。
    /// </summary>
    /// <param name="targetVersion">
    /// 要等待的目标版本，或者 <see langword="null"/>（仅刷新一次）。
    /// 如果指定版本，则该方法持续刷新，直到成员资格
    /// 至少达到该版本。
    /// </param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <param name="requireFresh">
    /// 是否要求对权威数据源的观察在调用此方法之后开始。
    /// </param>
    /// <remarks>
    /// 当 <paramref name="requireFresh"/> 为 <see langword="true"/> 时，实现可以通过
    /// 完整读取或产生并发布完整成员资格视图的写入满足新鲜度屏障。
    /// </remarks>
    Task Refresh(MembershipVersion? targetVersion, CancellationToken cancellationToken, bool requireFresh = false);

    /// <summary>
    /// 处理通过 Gossip 接收的成员资格快照。
    /// </summary>
    /// <param name="snapshot">通过 Gossip 传播的快照。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    Task ProcessGossipSnapshot(MembershipTableSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// 更新本地 Silo 的“我仍存活”时间戳。
    /// 对于被动系统，这会写入成员表。
    /// 对于主动系统，这可能不执行任何操作（采用不同的心跳处理方式）。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    Task UpdateIAmAlive(CancellationToken cancellationToken);
}
