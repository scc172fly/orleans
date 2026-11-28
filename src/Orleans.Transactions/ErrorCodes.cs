
namespace Orleans.Transactions
{
    /// <summary>
    /// Orleans 事务错误代码。
    /// </summary>
    internal enum OrleansTransactionsErrorCode
    {
        /// <summary>
        /// Orleans 事务错误代码的起始值。
        /// </summary>
        OrleansTransactions = 1 << 17,
        // TODO - jbragg - 为事务错误添加错误代码。
    }
}
