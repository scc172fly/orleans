using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义用于将交易协议信息发送到指定交易资源的 grain 扩展名。
    /// </summary>
    public interface ITransactionalResourceExtension : IGrainExtension
    {
        /// <summary>
        /// 在指定资源上提交只读交易 。
        /// </summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易对资源进行读写的次数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <returns>其结果是资源报告的交易状况。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("CommitReadOnly")]
        Task<TransactionalStatus> CommitReadOnly(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp);

        /// <summary>
        /// 在指定资源上提交只读交易 。
        /// </summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易对资源进行读写的次数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        /// <returns>其结果是资源报告的交易状况。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("1BB071FE")]
        Task<TransactionalStatus> CommitReadOnly(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, CancellationToken cancellationToken)
            => CommitReadOnly(resourceId, transactionId, accessCount, timeStamp);

        /// <summary>中止交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Abort")]
        Task Abort(string resourceId, Guid transactionId);

        /// <summary>中止交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("BD051D23")]
        Task Abort(string resourceId, Guid transactionId, CancellationToken cancellationToken)
            => Abort(resourceId, transactionId);

        /// <summary>取消交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Cancel")]
        Task Cancel(string resourceId, Guid transactionId, DateTime timeStamp, TransactionalStatus status);

        /// <summary>取消交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("80028AB9")]
        Task Cancel(string resourceId, Guid transactionId, DateTime timeStamp, TransactionalStatus status, CancellationToken cancellationToken)
            => Cancel(resourceId, transactionId, timeStamp, status);

        /// <summary>确认一项交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("Confirm")]
        Task Confirm(string resourceId, Guid transactionId, DateTime timeStamp);

        /// <summary>确认一项交易。</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [Alias("5DDDE6F0")]
        Task Confirm(string resourceId, Guid transactionId, DateTime timeStamp, CancellationToken cancellationToken)
            => Confirm(resourceId, transactionId, timeStamp);

        /// <summary>
        /// 准备进行交易的指定资源。
        /// </summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易对资源进行读写的次数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="transactionManager">交易经理协调交易。</param>
        /// <returns>一项代表单向操作的任务。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("Prepare")]
        Task Prepare(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager);

        /// <summary>准备交易</summary>
        /// <param name="resourceId">交易资源识别符。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">资源获取次数。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="transactionManager">交易经理</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [AlwaysInterleave]
        [Transaction(TransactionOption.Suppress)]
        [OneWay]
        [Alias("2ADCC608")]
        Task Prepare(string resourceId, Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager, CancellationToken cancellationToken)
            => Prepare(resourceId, transactionId, accessCount, timeStamp, transactionManager);
    }
}
