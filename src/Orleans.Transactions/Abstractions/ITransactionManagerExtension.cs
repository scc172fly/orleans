using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义用于将事务协议消息分派给命名事务管理器的 Grain 扩展。
    /// </summary>
    public interface ITransactionManagerExtension : IGrainExtension
    {
        /// <summary>
        /// 请求指定事务管理器准备并提交事务。
        /// </summary>
        /// <param name="resourceId">事务管理器资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务对事务管理器执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="writeResources">在事务期间执行过写入的参与者。</param>
        /// <param name="totalParticipants">事务参与者总数。</param>
        /// <returns>其结果为最终事务状态的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("PrepareAndCommit")]
        Task<TransactionalStatus> PrepareAndCommit(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, List<ParticipantId> writeResources, int totalParticipants);

        /// <summary>
        /// 请求指定事务管理器准备并提交事务。
        /// </summary>
        /// <param name="resourceId">事务管理器资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务对事务管理器执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="writeResources">在事务期间执行过写入的参与者。</param>
        /// <param name="totalParticipants">事务参与者总数。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>其结果为最终事务状态的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("B024EFA6")]
        Task<TransactionalStatus> PrepareAndCommit(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, List<ParticipantId> writeResources, int totalParticipants, CancellationToken cancellationToken)
            => PrepareAndCommit(resourceId, transactionId, accessCount, timeStamp, writeResources, totalParticipants);

        /// <summary>
        /// 向指定事务管理器报告参与者的准备结果。
        /// </summary>
        /// <param name="resourceId">事务管理器资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timestamp">事务提交时间戳。</param>
        /// <param name="resource">报告准备结果的参与者。</param>
        /// <param name="status">参与者的准备结果。</param>
        /// <returns>表示单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Prepared")]
        Task Prepared(string resourceId, Guid transactionId, DateTime timestamp, ParticipantId resource, TransactionalStatus status);

        /// <summary>
        /// 向指定事务管理器报告参与者的准备结果。
        /// </summary>
        /// <param name="resourceId">事务管理器资源的标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timestamp">事务提交时间戳。</param>
        /// <param name="resource">报告准备结果的参与者。</param>
        /// <param name="status">参与者的准备结果。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>表示单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("12BEFA17")]
        Task Prepared(string resourceId, Guid transactionId, DateTime timestamp, ParticipantId resource, TransactionalStatus status, CancellationToken cancellationToken)
            => Prepared(resourceId, transactionId, timestamp, resource, status);

        /// <summary>报告某个参与者正在等待事务结果。</summary>
        /// <param name="resourceId">事务管理器资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="resource">报告方参与者。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Ping")]
        Task Ping(string resourceId, Guid transactionId, DateTime timeStamp, ParticipantId resource);

        /// <summary>报告某个参与者正在等待事务结果。</summary>
        /// <param name="resourceId">事务管理器资源标识符。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="resource">报告方参与者。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("AC4A9AEB")]
        Task Ping(string resourceId, Guid transactionId, DateTime timeStamp, ParticipantId resource, CancellationToken cancellationToken)
            => Ping(resourceId, transactionId, timeStamp, resource);
    }
}
