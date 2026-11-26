
namespace Orleans.Transactions
{
    /// <summary>
    /// Orleans 交易错误代码
    /// </summary>
    internal enum OrleansTransactionsErrorCode
    {
        /// <summary>
        /// orleans 交易错误代码的开始
        /// </summary>
        OrleansTransactions = 1 << 17,
        // TODO - jbragg - 为交易错误添加错误代码
    }
}
