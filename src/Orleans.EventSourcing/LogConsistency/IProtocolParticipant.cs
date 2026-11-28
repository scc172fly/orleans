using System.Threading;
using System.Threading.Tasks;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 参与多集群日志一致性协议的 Grain 的接口。
    /// </summary>
    public interface ILogConsistencyProtocolParticipant : IGrain
    {
        /// <summary>
        /// 在同一调度器上，紧接用户级 OnActivateAsync 之前调用。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("PreActivateProtocolParticipant")]
        Task PreActivateProtocolParticipant();

        /// <summary>
        /// 在同一调度器上，紧接用户级 OnActivateAsync 之前调用。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("0DB087C8")]
        Task PreActivateProtocolParticipant(CancellationToken cancellationToken)
            => PreActivateProtocolParticipant();

        /// <summary>
        /// 在同一调度器上，紧接用户级 OnActivateAsync 之后调用。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("PostActivateProtocolParticipant")]
        Task PostActivateProtocolParticipant();

        /// <summary>
        /// 在同一调度器上，紧接用户级 OnActivateAsync 之后调用。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("22FD7D72")]
        Task PostActivateProtocolParticipant(CancellationToken cancellationToken)
            => PostActivateProtocolParticipant();

        /// <summary>
        /// 在同一调度器上，紧接用户级 OnDeactivateAsync 之后调用。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("DeactivateProtocolParticipant")]
        Task DeactivateProtocolParticipant();

        /// <summary>
        /// 在同一调度器上，紧接用户级 OnDeactivateAsync 之后调用。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("A36FC884")]
        Task DeactivateProtocolParticipant(CancellationToken cancellationToken)
            => DeactivateProtocolParticipant();
    }

    /// <summary>
    /// 用于标记表示协议消息的类的接口。
    /// 所有此类均必须可序列化。
    /// </summary>
    public interface ILogConsistencyProtocolMessage
    {
    }
}
