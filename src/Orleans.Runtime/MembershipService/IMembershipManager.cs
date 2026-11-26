#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.MembershipService;

/// <summary>
/// 管理当地 silo的集群成员状况和生命周期。
/// 这是成员管理的主要抽象概念,支持
/// 既包括被动(基于表格)的执行,也包括主动(基于共识)的执行。
/// </summary>
/// <remarks>
/// <para>
/// 执行方必须参与 silo 生命周期和健康检查。
/// 这与 <see cref="IClusterHealthMonitor"/>的格局相同。
/// </para>
/// </remarks>
internal interface IMembershipManager : ILifecycleParticipant<ISiloLifecycle>, IHealthCheckParticipant
{
    /// <summary>
    /// 获得当前会员的快照。
    /// </summary>
    MembershipTableSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// 获得一连串的成员更新。
    /// 成员认为有变化时,随时公布最新情况。
    /// 消费者应处理更新工作。
    /// </summary>
    IAsyncEnumerable<MembershipTableSnapshot> MembershipUpdates { get; }

    /// <summary>
    /// 获取本地 silo的当前状态 。
    /// </summary>
    SiloStatus LocalSiloStatus { get; }

    /// <summary>
    /// 更新该组群中本地 silo的地位。
    /// </summary>
    /// <param name="status">新状态。</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>任务在状态更新后完成。</returns>
    /// <exception cref="OrleansException">如果更新失败。</exception>
    Task UpdateLocalStatus(SiloStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// 要求宣布一个 silo 死亡。
    /// 对于被动系统,这触发了投票/杀戮过程。
    /// 对于运行中的系统,这可能会引发观点改变提案。
    /// </summary>
    /// <param name="silo">下 silo 杀。</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>如果真实 silo 被杀死,否则是假的。</returns>
    Task<bool> TryKillSilo(SiloAddress silo, CancellationToken cancellationToken);

    /// <summary>
    /// 要求怀疑 silo (可能导致杀害)。
    /// 对于被动系统,这增加了一票。
    /// 对于运行中的系统,这可能是无操作(FD处理此事)。
    /// </summary>
    /// <param name="silo">怀疑的 silo 。</param>
    /// <param name="indirectProbingSilo">下 silo 也检测到故障,或 <see langword="null"/>.</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>如果采取了行动,是真实的。</returns>
    Task<bool> TrySuspectSilo(SiloAddress silo, SiloAddress? indirectProbingSilo, CancellationToken cancellationToken);

    /// <summary>
    /// 重新从真相来源了解成员观点。
    /// 对于被动系统,这来自成员表。
    /// 对于运行中的系统,这可能是不操作或要求获取最新视图。
    /// </summary>
    /// <param name="targetVersion">
    /// 等待的目标版本, 或 <see langword="null"/> 的单一更新 。
    /// 如果有指定,该方法将继续更新,直到成员
    /// 至少达到此版本 。
    /// </param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <param name="requireFresh">
    /// 是否要求从源真相观察开始,而这种观察是在采用这种方法之后开始的。
    /// </param>
    /// <remarks>
    /// <paramref name="requireFresh"/> 是 <see langword="true"/>时,执行可以满足
    /// 使用全读或全写来制作和出版完整的会员观点的新屏障。
    /// </remarks>
    Task Refresh(MembershipVersion? targetVersion, CancellationToken cancellationToken, bool requireFresh = false);

    /// <summary>
    /// 处理通过八卦收到的会员简况。
    /// </summary>
    /// <param name="snapshot">流言蜚语的快照</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    Task ProcessGossipSnapshot(MembershipTableSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// 更新本地 silo的“ 我还活着” 时间戳 。
    /// 对于被动系统,这是写在成员表上。
    /// 对于运行中的系统,这可能是不操作(心跳处理方式不同)。
    /// </summary>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    Task UpdateIAmAlive(CancellationToken cancellationToken);
}
