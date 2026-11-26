
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 跟踪交易代理活动。
    /// </summary>
    public interface ITransactionAgentStatistics
    {
        /// <summary>
        /// 记录交易已经开始。
        /// </summary>
        void TrackTransactionStarted();

        /// <summary>
        /// 获取已经开始的交易数量。
        /// </summary>
        long TransactionsStarted { get; }

        /// <summary>
        /// 交易成功完成的记录。
        /// </summary>
        void TrackTransactionSucceeded();

        /// <summary>
        /// 获取成功完成的交易数量。
        /// </summary>
        long TransactionsSucceeded { get; }

        /// <summary>
        /// 记录交易失败的记录。
        /// </summary>
        void TrackTransactionFailed();

        /// <summary>
        /// 获取失败的交易数量。
        /// </summary>
        long TransactionsFailed { get; }

        /// <summary>
        /// 交易因交易代理中断而被拒绝的记录。
        /// </summary>
        void TrackTransactionThrottled();

        /// <summary>
        /// 获得因交易代理中断而被拒绝的交易数量。
        /// </summary>
        long TransactionsThrottled { get; }
    }
}
