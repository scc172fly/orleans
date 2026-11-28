using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义用于将事务协议消息分派给命名事务资源的 Grain 扩展。
    /// </summary>
    public interface ITransactionalResourceExtension : IGrainExtension
    {
        /// <summary>
        /// 在指定资源上提交只读事务。
        /// </summary>
        /// <param name="resourceId">事务资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务对资源执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <returns>其结果为资源所报告事务状态的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("CommitReadOnly")]
        Task<TransactionalStatus> CommitReadOnly(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp);

        /// <summary>
        /// 在指定资源上提交只读事务。
        /// </summary>
        /// <param name="resourceId">事务资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务对资源执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>其结果为资源所报告事务状态的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("1BB071FE")]
        Task<TransactionalStatus> CommitReadOnly(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, CancellationToken cancellationToken)
            => CommitReadOnly(resourceId, transactionId, accessCount, timeStamp);

        /// <summary>中止事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Abort")]
        Task Abort(string resourceId, Guid transactionId);

        /// <summary>中止事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("BD051D23")]
        Task Abort(string resourceId, Guid transactionId, CancellationToken cancellationToken)
            => Abort(resourceId, transactionId);

        /// <summary>取消事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Cancel")]
        Task Cancel(string resourceId, Guid transactionId, DateTime timeStamp, TransactionalStatus status);

        /// <summary>取消事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("80028AB9")]
        Task Cancel(string resourceId, Guid transactionId, DateTime timeStamp, TransactionalStatus status, CancellationToken cancellationToken)
            => Cancel(resourceId, transactionId, timeStamp, status);

        /// <summary>确认事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Confirm")]
        Task Confirm(string resourceId, Guid transactionId, DateTime timeStamp);

        /// <summary>确认事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("5DDDE6F0")]
        Task Confirm(string resourceId, Guid transactionId, DateTime timeStamp, CancellationToken cancellationToken)
            => Confirm(resourceId, transactionId, timeStamp);

        /// <summary>
        /// 使指定资源做好提交事务的准备。
        /// </summary>
        /// <param name="resourceId">事务资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务对资源执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="transactionManager">协调该事务的事务管理器。</param>
        /// <returns>表示单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Prepare")]
        Task Prepare(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager);

        /// <summary>准备事务。</summary>
        /// <param name="resourceId">事务资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">资源访问次数。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="transactionManager">事务管理器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("2ADCC608")]
        Task Prepare(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager, CancellationToken cancellationToken)
            => Prepare(resourceId, transactionId, accessCount, timeStamp, transactionManager);
    }
}
