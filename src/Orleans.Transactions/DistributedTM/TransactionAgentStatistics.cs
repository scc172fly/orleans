using System.Diagnostics.Metrics;
using System.Threading;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 记录事务代理的累计处理结果，并将其作为 Orleans 指标发布。
    /// </summary>
    public class TransactionAgentStatistics : ITransactionAgentStatistics
    {
        private const string TRANSACTIONS_STARTED = "orleans-transactions-started";
        private const string TRANSACTIONS_SUCCESSFUL = "orleans-transactions-successful";
        private const string TRANSACTIONS_FAILED = "orleans-transactions-failed";
        private const string TRANSACTIONS_THROTTLED = "orleans-transactions-throttled";
        private readonly ObservableCounter<long> _transactionsStartedCounter;
        private readonly ObservableCounter<long> _transactionsSuccessfulCounter;
        private readonly ObservableCounter<long> _transactionsFailedCounter;
        private readonly ObservableCounter<long> _transactionsThrottledCounter;

        private long _transactionsStarted;
        private long _transactionsSucceeded;
        private long _transactionsFailed;
        private long _transactionsThrottled;

        /// <summary>
        /// 使用专用的 Orleans Meter 初始化新实例。
        /// </summary>
        public TransactionAgentStatistics()
            : this(new Meter("Microsoft.Orleans"))
        {
        }

        /// <summary>
        /// 使用 Orleans 运行时 Meter 初始化新实例。
        /// </summary>
        /// <param name="instruments">提供用于发布事务计数器的 Meter 的 Orleans 指标工具。</param>
        public TransactionAgentStatistics(OrleansInstruments instruments)
            : this(instruments.Meter)
        {
        }

        private TransactionAgentStatistics(Meter meter)
        {
            _transactionsStartedCounter = meter.CreateObservableCounter<long>(TRANSACTIONS_STARTED, () => new(TransactionsStarted));
            _transactionsSuccessfulCounter = meter.CreateObservableCounter<long>(TRANSACTIONS_SUCCESSFUL, () => new(TransactionsSucceeded));
            _transactionsFailedCounter = meter.CreateObservableCounter<long>(TRANSACTIONS_FAILED, () => new(TransactionsFailed));
            _transactionsThrottledCounter = meter.CreateObservableCounter<long>(TRANSACTIONS_THROTTLED, () => new(TransactionsThrottled));
        }

        /// <summary>
        /// 获取已启动的事务总数。
        /// </summary>
        public long TransactionsStarted => _transactionsStarted;

        /// <summary>
        /// 获取已成功完成的事务总数。
        /// </summary>
        public long TransactionsSucceeded => _transactionsSucceeded;

        /// <summary>
        /// 获取失败或被中止的事务总数。
        /// </summary>
        public long TransactionsFailed => _transactionsFailed;

        /// <summary>
        /// 获取因负载削减而被拒绝的事务总数。
        /// </summary>
        public long TransactionsThrottled => _transactionsThrottled;

        /// <summary>
        /// 记录事务已启动。
        /// </summary>
        public void TrackTransactionStarted()
        {
            Interlocked.Increment(ref _transactionsStarted);
        }

        /// <summary>
        /// 记录事务已成功完成。
        /// </summary>
        public void TrackTransactionSucceeded()
        {
            Interlocked.Increment(ref _transactionsSucceeded);
        }

        /// <summary>
        /// 记录事务失败或被中止。
        /// </summary>
        public void TrackTransactionFailed()
        {
            Interlocked.Increment(ref _transactionsFailed);
        }

        /// <summary>
        /// 记录事务因负载削减而被拒绝。
        /// </summary>
        public void TrackTransactionThrottled()
        {
            Interlocked.Increment(ref _transactionsThrottled);
        }

        /// <summary>
        /// 创建事务代理统计数据的独立可变快照。
        /// </summary>
        /// <param name="initialStatistics">用于初始化快照计数器的统计数据。</param>
        /// <returns>使用 <paramref name="initialStatistics"/> 的当前值初始化的统计快照。</returns>
        public static ITransactionAgentStatistics Copy(ITransactionAgentStatistics initialStatistics)
        {
            return new TransactionAgentStatisticsSnapshot(initialStatistics);
        }

        private sealed class TransactionAgentStatisticsSnapshot : ITransactionAgentStatistics
        {
            private long _transactionsStarted;
            private long _transactionsSucceeded;
            private long _transactionsFailed;
            private long _transactionsThrottled;

            public TransactionAgentStatisticsSnapshot(ITransactionAgentStatistics statistics)
            {
                _transactionsStarted = statistics.TransactionsStarted;
                _transactionsSucceeded = statistics.TransactionsSucceeded;
                _transactionsFailed = statistics.TransactionsFailed;
                _transactionsThrottled = statistics.TransactionsThrottled;
            }

            public long TransactionsStarted => _transactionsStarted;

            public long TransactionsSucceeded => _transactionsSucceeded;

            public long TransactionsFailed => _transactionsFailed;

            public long TransactionsThrottled => _transactionsThrottled;

            public void TrackTransactionStarted() => Interlocked.Increment(ref _transactionsStarted);

            public void TrackTransactionSucceeded() => Interlocked.Increment(ref _transactionsSucceeded);

            public void TrackTransactionFailed() => Interlocked.Increment(ref _transactionsFailed);

            public void TrackTransactionThrottled() => Interlocked.Increment(ref _transactionsThrottled);
        }
    }
}
