using System.Diagnostics.Metrics;
using System.Threading;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 记录交易代理商的累计结果,并将其作为 Orleans 衡量标准予以公布。
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
        /// 使用专用 Orleans 仪初始化新实例。
        /// </summary>
        public TransactionAgentStatistics()
            : this(new Meter("Microsoft.Orleans"))
        {
        }

        /// <summary>
        /// 使用 Orleans 运行时计初始化新实例。
        /// </summary>
        /// <param name="instruments">下 Orleans 提供用于公布交易柜台的仪表的工具。</param>
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
        /// 获取已经开始的交易的总数 。
        /// </summary>
        public long TransactionsStarted => _transactionsStarted;

        /// <summary>
        /// 获得成功完成的交易总数。
        /// </summary>
        public long TransactionsSucceeded => _transactionsSucceeded;

        /// <summary>
        /// 获取失败或中止的交易的总数。
        /// </summary>
        public long TransactionsFailed => _transactionsFailed;

        /// <summary>
        /// 获得通过加载停放拒绝的交易总数。
        /// </summary>
        public long TransactionsThrottled => _transactionsThrottled;

        /// <summary>
        /// 记录交易已经开始。
        /// </summary>
        public void TrackTransactionStarted()
        {
            Interlocked.Increment(ref _transactionsStarted);
        }

        /// <summary>
        /// 记录交易成功完成的记录。
        /// </summary>
        public void TrackTransactionSucceeded()
        {
            Interlocked.Increment(ref _transactionsSucceeded);
        }

        /// <summary>
        /// 记录交易失败或中止。
        /// </summary>
        public void TrackTransactionFailed()
        {
            Interlocked.Increment(ref _transactionsFailed);
        }

        /// <summary>
        /// 记录显示交易被卸载后拒绝
        /// </summary>
        public void TrackTransactionThrottled()
        {
            Interlocked.Increment(ref _transactionsThrottled);
        }

        /// <summary>
        /// 创建独立的、可变的交易代理统计数据快照。
        /// </summary>
        /// <param name="initialStatistics">统计,其当前对应值的对比值初始化快照。</param>
        /// <returns>与 <paramref name="initialStatistics"/>当前值一起草签的统计数据。</returns>
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
