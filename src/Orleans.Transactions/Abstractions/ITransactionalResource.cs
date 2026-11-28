using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 允许组件成为事务参与者的接口。
    /// </summary>
    public interface ITransactionalResource
    {
        /// <summary>
        /// 由 TA 发送给只读事务所有参与者的请求（单阶段提交）。
        /// 参与者在提交或中止读取后作出响应。
        /// </summary>
        /// <param name="transactionId">要准备的事务 ID。</param>
        /// <param name="accessCount">该事务对当前参与者执行的读写次数。</param>
        /// <param name="timeStamp">该事务的提交时间戳。</param>
        /// <returns>其结果为资源所报告事务状态的任务。</returns>
        Task<TransactionalStatus> CommitReadOnly(Guid transactionId, AccessCounter accessCount, DateTime timeStamp);

        /// <summary>
        /// 提交只读事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务执行的资源访问次数。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>事务状态。</returns>
        Task<TransactionalStatus> CommitReadOnly(Guid transactionId, AccessCounter accessCount, DateTime timeStamp, CancellationToken cancellationToken)
            => CommitReadOnly(transactionId, accessCount, timeStamp);

        /// <summary>
        /// 由 TA 向 TM 以外的所有参与者发送的单向消息。
        /// </summary>
        /// <param name="transactionId">要准备的事务 ID。</param>
        /// <param name="accessCount">该事务对当前参与者执行的读写次数。</param>
        /// <param name="timeStamp">该事务的提交时间戳。</param>
        /// <param name="transactionManager">该事务的事务管理器。</param>
        /// <returns>表示该操作的任务。</returns>
        Task Prepare(Guid transactionId, AccessCounter accessCount,
            DateTime timeStamp, ParticipantId transactionManager);

        /// <summary>
        /// 准备事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务执行的资源访问次数。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="transactionManager">事务管理器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Prepare(Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager, CancellationToken cancellationToken)
            => Prepare(transactionId, accessCount, timeStamp, transactionManager);

        /// <summary>
        /// 由 TA 向参与者发送的单向消息，告知参与者事务已中止。
        /// </summary>
        /// <param name="transactionId">已中止事务的 ID。</param>
        Task Abort(Guid transactionId);

        /// <summary>
        /// 中止事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Abort(Guid transactionId, CancellationToken cancellationToken) => Abort(transactionId);

        /// <summary>
        /// 由 TM 向参与者发送的单向消息，告知参与者事务已中止。
        /// </summary>
        /// <param name="transactionId">已中止事务的 ID。</param>
        /// <param name="timeStamp">已中止事务的提交时间戳。</param>
        /// <param name="status">中止原因。</param>
        Task Cancel(Guid transactionId, DateTime timeStamp, TransactionalStatus status);

        /// <summary>
        /// 取消事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Cancel(Guid transactionId, DateTime timeStamp, TransactionalStatus status, CancellationToken cancellationToken)
            => Cancel(transactionId, timeStamp, status);

        /// <summary>
        /// 由 TM 发送给参与者的请求，告知参与者事务已提交。
        /// 参与者清理所有准备记录后作出响应。
        /// </summary>
        /// <param name="transactionId">已提交事务的 ID。</param>
        /// <param name="timeStamp">已提交事务的提交时间戳。</param>
        Task Confirm(Guid transactionId, DateTime timeStamp);

        /// <summary>
        /// 确认事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Confirm(Guid transactionId, DateTime timeStamp, CancellationToken cancellationToken)
            => Confirm(transactionId, timeStamp);
    }
}
