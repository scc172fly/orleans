using System;
using System.Threading.Tasks;

namespace Orleans.Transactions
{
    /// <summary>
    /// 为 Silo 协调事务的创建、决议和中止。
    /// </summary>
    /// <remarks>
    /// 每个 Silo 有一个事务代理。
    /// </remarks>
    public interface ITransactionAgent
    {
        /// <summary>
        /// 启动新事务。
        /// </summary>
        /// <param name="readOnly">指示事务是否仅允许读取操作的值。</param>
        /// <param name="timeout">经过此时间间隔后，事务可以被中止。</param>
        /// <returns>新事务的上下文信息。</returns>
        Task<TransactionInfo> StartTransaction(bool readOnly, TimeSpan timeout);

        /// <summary>
        /// 通过提交或中止来完成事务决议。
        /// </summary>
        /// <param name="transactionInfo">事务上下文信息。</param>
        /// <returns>
        /// 决议状态，以及导致该结果的底层异常（如果存在）。
        /// </returns>
        Task<(TransactionalStatus Status, Exception? exception)> Resolve(TransactionInfo transactionInfo);

        /// <summary>
        /// 中止事务。
        /// </summary>
        /// <param name="transactionInfo">事务上下文信息。</param>
        /// <returns>表示中止操作的 <see cref="Task"/>。</returns>
        /// <remarks>此操作在完成时不会传播事务协议异常。</remarks>
        Task Abort(TransactionInfo transactionInfo);
    }
}
