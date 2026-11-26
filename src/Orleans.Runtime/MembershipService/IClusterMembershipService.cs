using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 与分组成员进行查询和互动的职能。
    /// </summary>
    public interface IClusterMembershipService
    {
        /// <summary>
        /// 获得最新的分组成员简况。
        /// </summary>
        /// <value>当前快照。</value>
        ClusterMembershipSnapshot CurrentSnapshot { get; }

        /// <summary>
        /// 收集了大量会员最新资料
        /// </summary>
        /// <remarks>
        /// 版本前端,会籍会用卡通式更改。 同一版本的更新可以
        /// 将死行删除或提高该观察员对缺失 silo 身份的知情度。
        /// </remarks>
        /// <value>成员最新情况。</value>
        IAsyncEnumerable<ClusterMembershipSnapshot> MembershipUpdates { get; }

        /// <summary>
        /// 如果组群成员不达到或超过规定的最低限度版本,则更新其成员。
        /// </summary>
        /// <param name="minimumVersion">最小版本。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="ValueTask"/> 代表所完成的工作。</returns>
        ValueTask Refresh(MembershipVersion minimumVersion = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// 单方面宣布指定的 silo 已失效。
        /// </summary>
        /// <param name="siloAddress">下 silo 地址已声明失效 。</param>
        /// <returns><see langword="true"/> 如果 silo 被逐出家园, <see langword="false"/> 否则。</returns>
        Task<bool> TryKill(SiloAddress siloAddress);
    }
}
