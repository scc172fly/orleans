using System.Threading;
using System.Threading.Tasks;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// Grain 界面的接口 grains 参加多组对地统一议定书。
    /// </summary>
    public interface ILogConsistencyProtocolParticipant : IGrain
    {
        /// <summary>
        /// 直接呼叫用户级别 OnActivateAsync, 在同一排程器上。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("PreActivateProtocolParticipant")]
        Task PreActivateProtocolParticipant();

        /// <summary>
        /// 直接调用在同一排程器上的用户级别 OnActivateAsync。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("0DB087C8")]
        Task PreActivateProtocolParticipant(CancellationToken cancellationToken)
            => PreActivateProtocolParticipant();

        /// <summary>
        /// 在同一排程器的用户级别 OnActivateAsync后立即调用。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("PostActivateProtocolParticipant")]
        Task PostActivateProtocolParticipant();

        /// <summary>
        /// 在同一排程器的用户级别 OnActivateAsync后立即调用。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("22FD7D72")]
        Task PostActivateProtocolParticipant(CancellationToken cancellationToken)
            => PostActivateProtocolParticipant();

        /// <summary>
        /// 在同一排程器的用户级别 OnDeactivateAsync后立即调用。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("DeactivateProtocolParticipant")]
        Task DeactivateProtocolParticipant();

        /// <summary>
        /// 在同一排程器的用户级别 OnDeactivateAsync后立即调用。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("A36FC884")]
        Task DeactivateProtocolParticipant(CancellationToken cancellationToken)
            => DeactivateProtocolParticipant();
    }

    /// <summary>
    /// 来标记代表协议信件的分类。
    /// 所有这类类别都必须具有连号性。
    /// </summary>
    public interface ILogConsistencyProtocolMessage
    {
    }
}
