
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 跟踪事务代理的活动。
    /// </summary>
    public interface ITransactionAgentStatistics
    {
        /// <summary>
        /// 记录事务已启动。
        /// </summary>
        void TrackTransactionStarted();

        /// <summary>
        /// 获取已启动的事务数量。
        /// </summary>
        long TransactionsStarted { get; }

        /// <summary>
        /// 记录事务已成功完成。
        /// </summary>
        void TrackTransactionSucceeded();

        /// <summary>
        /// 获取已成功完成的事务数量。
        /// </summary>
        long TransactionsSucceeded { get; }

        /// <summary>
        /// 记录事务失败。
        /// </summary>
        void TrackTransactionFailed();

        /// <summary>
        /// 获取失败的事务数量。
        /// </summary>
        long TransactionsFailed { get; }

        /// <summary>
        /// 记录因事务代理限流而被拒绝的事务。
        /// </summary>
        void TrackTransactionThrottled();

        /// <summary>
        /// 获取因事务代理限流而被拒绝的事务数量。
        /// </summary>
        long TransactionsThrottled { get; }
    }
}
