using System;
using System.Threading.Tasks;

namespace Orleans.Transactions
{
    /// <summary>
    /// 协调交易的产生、解决和堕胎 silo.
    /// </summary>
    /// <remarks>
    /// silo 有一个交易代理。
    /// </remarks>
    public interface ITransactionAgent
    {
        /// <summary>
        /// 开始新的交易。
        /// </summary>
        /// <param name="readOnly">表明交易是否允许只读业务的价值。</param>
        /// <param name="timeout">交易有资格中止的间隔。</param>
        /// <returns>新的交易背景信息。</returns>
        Task<TransactionInfo> StartTransaction(bool readOnly, TimeSpan timeout);

        /// <summary>
        /// 以实施或中止交易的方式解决一项交易。
        /// </summary>
        /// <param name="transactionInfo">交易背景信息。</param>
        /// <returns>
        /// 如果决议地位和基本例外有助于取得结果,则其地位和基本例外。
        /// </returns>
        Task<(TransactionalStatus Status, Exception? exception)> Resolve(TransactionInfo transactionInfo);

        /// <summary>
        /// 中止交易。
        /// </summary>
        /// <param name="transactionInfo">交易背景信息。</param>
        /// <returns>A <see cref="Task"/> 代表中止操作。</returns>
        /// <remarks>此项操作在不传播交易协议例外的情况下完成。</remarks>
        Task Abort(TransactionInfo transactionInfo);
    }
}
