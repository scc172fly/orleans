using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义 grain 用于向指定交易管理人发送交易协议信息扩展名。
    /// </summary>
    public interface ITransactionManagerExtension : IGrainExtension
    {
        /// <summary>
        /// 请指定的交易管理人准备并进行一项交易。
        /// </summary>
        /// <param name="resourceId">交易经理资源的身份识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易经理在交易中读写的次数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="writeResources">交易期间写作的参与者。</param>
        /// <param name="totalParticipants">交易参与者总数。</param>
        /// <returns>其结果是最终交易状况。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("PrepareAndCommit")]
        Task<TransactionalStatus> PrepareAndCommit(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, List<ParticipantId> writeResources, int totalParticipants);

        /// <summary>
        /// 请指定的交易管理人准备并进行一项交易。
        /// </summary>
        /// <param name="resourceId">交易经理资源的身份识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易经理在交易中读写的次数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="writeResources">交易期间写作的参与者。</param>
        /// <param name="totalParticipants">交易参与者总数。</param>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        /// <returns>其结果是最终交易状况。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("B024EFA6")]
        Task<TransactionalStatus> PrepareAndCommit(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, List<ParticipantId> writeResources, int totalParticipants, CancellationToken cancellationToken)
            => PrepareAndCommit(resourceId, transactionId, accessCount, timeStamp, writeResources, totalParticipants);

        /// <summary>
        /// 向指定的交易管理人报告为参与者做好准备的结果。
        /// </summary>
        /// <param name="resourceId">交易经理资源的身份识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timestamp">交易承诺时间戳。</param>
        /// <param name="resource">参与者报告其准备结果。</param>
        /// <param name="status">参与者的准备结果</param>
        /// <returns>一项代表单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Prepared")]
        Task Prepared(string resourceId, Guid transactionId, DateTime timestamp, ParticipantId resource, TransactionalStatus status);

        /// <summary>
        /// 向指定的交易管理人报告为参与者做好准备的结果。
        /// </summary>
        /// <param name="resourceId">交易经理资源的身份识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timestamp">交易承诺时间戳。</param>
        /// <param name="resource">参与者报告其准备结果。</param>
        /// <param name="status">参与者的准备结果</param>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        /// <returns>一项代表单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("12BEFA17")]
        Task Prepared(string resourceId, Guid transactionId, DateTime timestamp, ParticipantId resource, TransactionalStatus status, CancellationToken cancellationToken)
            => Prepared(resourceId, transactionId, timestamp, resource, status);

        /// <summary>参与者报告正在等待交易结果。</summary>
        /// <param name="resourceId">交易经理资源识别特征。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="resource">提交报告的参与者。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Ping")]
        Task Ping(string resourceId, Guid transactionId, DateTime timeStamp, ParticipantId resource);

        /// <summary>参与者报告正在等待交易结果。</summary>
        /// <param name="resourceId">交易经理资源识别特征。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="resource">提交报告的参与者。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("AC4A9AEB")]
        Task Ping(string resourceId, Guid transactionId, DateTime timeStamp, ParticipantId resource, CancellationToken cancellationToken)
            => Ping(resourceId, transactionId, timeStamp, resource);
    }
}
