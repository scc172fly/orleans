using System;
using Microsoft.Extensions.Options;
using Orleans.Internal.Trasactions;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 检测事务启动速率是否超过配置的负载削减限制。
    /// </summary>
    public interface ITransactionOverloadDetector
    {
        /// <summary>
        /// 确定是否应因事务启动速率超过限制而拒绝新事务。
        /// </summary>
        /// <returns><see langword="true"/>，如果已启用事务负载削减且测得的速率超过限制；否则为 <see langword="false"/>。</returns>
        bool IsOverloaded();
    }

    /// <summary>
    /// 根据事务启动速率配置负载削减机制。
    /// </summary>
    public class TransactionRateLoadSheddingOptions
    {
        /// <summary>
        /// 获取或设置指示是否启用基于事务速率的负载削减机制的值。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 默认事务启动速率限制，单位为每秒事务数。
        /// </summary>
        public const double DEFAULT_LIMIT = 700;

        /// <summary>
        /// 获取或设置事务启动速率限制，单位为每秒事务数。
        /// </summary>
        public double Limit { get; set; } = DEFAULT_LIMIT;
    }

    /// <summary>
    /// 使用在一段时间内采样得到的加权事务启动速率检测事务过载。
    /// </summary>
    public class TransactionOverloadDetector : ITransactionOverloadDetector
    {
        private readonly ITransactionAgentStatistics statistics;
        private readonly TransactionRateLoadSheddingOptions options;
        private readonly PeriodicAction monitor;
        private readonly TimeProvider timeProvider;
        private ITransactionAgentStatistics lastStatistics;
        private double transactionStartedPerSecond;
        private DateTime lastCheckTime;
        private static readonly TimeSpan MetricsCheck = TimeSpan.FromSeconds(15);
        /// <summary>
        /// 初始化新的事务过载检测器。
        /// </summary>
        /// <param name="statistics">用于计算启动速率的事务代理累计统计数据。</param>
        /// <param name="options">基于事务速率的负载削减选项。</param>
        public TransactionOverloadDetector(ITransactionAgentStatistics statistics, IOptions<TransactionRateLoadSheddingOptions> options)
            : this(statistics, options, TimeProvider.System)
        {
        }

        internal TransactionOverloadDetector(ITransactionAgentStatistics statistics, IOptions<TransactionRateLoadSheddingOptions> options, TimeProvider timeProvider)
        {
            this.statistics = statistics;
            this.options = options.Value;
            this.timeProvider = timeProvider;
            var now = this.timeProvider.GetUtcNow().UtcDateTime;
            this.monitor = new PeriodicAction(MetricsCheck, this.RecordStatistics, now + MetricsCheck);
            this.lastStatistics = TransactionAgentStatistics.Copy(statistics);
            this.lastCheckTime = now;
        }

        private void RecordStatistics()
        {
            ITransactionAgentStatistics current = TransactionAgentStatistics.Copy(this.statistics);
            DateTime now = this.timeProvider.GetUtcNow().UtcDateTime;

            this.transactionStartedPerSecond = CalculateTps(this.lastStatistics.TransactionsStarted, this.lastCheckTime, current.TransactionsStarted, now);
            this.lastStatistics = current;
            this.lastCheckTime = now;
        }

        /// <summary>
        /// 确定加权事务启动速率是否超过配置的限制。
        /// </summary>
        /// <returns><see langword="true"/>，如果已启用负载削减且加权速率超过限制；否则为 <see langword="false"/>。</returns>
        public bool IsOverloaded()
        {
            if (!this.options.Enabled)
                return false;

            DateTime now = this.timeProvider.GetUtcNow().UtcDateTime;
            this.monitor.TryAction(now);
            double txPerSecondCurrently = CalculateTps(this.lastStatistics.TransactionsStarted, this.lastCheckTime, this.statistics.TransactionsStarted, now);
            // 按每秒事务数计算衰减的利用率。
            var aggregratedTxPerSecond = (this.transactionStartedPerSecond + (2.0 * txPerSecondCurrently)) / 3.0;

            return aggregratedTxPerSecond > this.options.Limit;
        }

        private static double CalculateTps(long startCounter, DateTime startTimeUtc, long currentCounter, DateTime curentTimeUtc)
        {
            TimeSpan deltaTime = curentTimeUtc - startTimeUtc;
            long deltaCounter = currentCounter - startCounter;
            return (deltaTime.TotalMilliseconds < 1000)
                ? deltaCounter
                : (deltaCounter * 1000.0) / deltaTime.TotalMilliseconds;
        }
    }
}
