using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供查询集群成员资格以及与其交互的功能。
    /// </summary>
    public interface IClusterMembershipService
    {
        /// <summary>
        /// 获取最新的集群成员资格快照。
        /// </summary>
        /// <value>当前快照。</value>
        ClusterMembershipSnapshot CurrentSnapshot { get; }

        /// <summary>
        /// 获取成员资格更新的可枚举集合。
        /// </summary>
        /// <remarks>
        /// 权威成员资格发生变更时，版本随之前进。相同版本的更新可以
        /// 清除死亡成员行，或增加此观察者对已缺失 Silo 标识的了解。
        /// </remarks>
        /// <value>成员资格更新。</value>
        IAsyncEnumerable<ClusterMembershipSnapshot> MembershipUpdates { get; }

        /// <summary>
        /// 如果当前集群成员资格版本低于指定的最低版本，则刷新它。
        /// </summary>
        /// <param name="minimumVersion">最低版本。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示所执行工作的 <see cref="ValueTask"/>。</returns>
        ValueTask Refresh(MembershipVersion minimumVersion = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 单方面宣告指定 Silo 已失效。
        /// </summary>
        /// <param name="siloAddress">被宣告失效的 Silo 地址。</param>
        /// <returns>如果 Silo 已被逐出，则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        Task<bool> TryKill(SiloAddress siloAddress);
    }
}
