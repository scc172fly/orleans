using System;
using Microsoft.Extensions.Options;
using Orleans.Internal.Trasactions;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 当交易启动率超过配置的卸载中断限制时检测 。
    /// </summary>
    public interface ITransactionOverloadDetector
    {
        /// <summary>
        /// 确定是否应当拒绝新的交易,因为交易启动率超过其限度。
        /// </summary>
        /// <returns><see langword="true"/> ,如果允许交易负荷停止,且所计量的比率超过其限度;否则, <see langword="false"/>。</returns>
        bool IsOverloaded();
    }

    /// <summary>
    /// 根据交易启动率配置已卸载的配置 。
    /// </summary>
    public class TransactionRateLoadSheddingOptions
    {
        /// <summary>
        /// 获取或设定一个值,以显示是否启用交易率负荷叠加 。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 默认交易起始率限制, 每秒交易中 。
        /// </summary>
        public const double DEFAULT_LIMIT = 700;

        /// <summary>
        /// 获取或设定交易起始率限制,每秒交易中。
        /// </summary>
        public double Limit { get; set; } = DEFAULT_LIMIT;
    }

    /// <summary>
    /// 使用一段时间内抽样的加权交易起始率,检测交易超负荷。
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
        /// 初始化新的交易超载探测器。
        /// </summary>
        /// <param name="statistics">计算起始率所用的交易代理的累积统计数据。</param>
        /// <param name="options">交易率减压选项。</param>
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
        /// 确定加权交易启动率是否超过配置的上限。
        /// </summary>
        /// <returns><see langword="true"/> ,当启用装载叠加而加权率超过限额时;否则, <see langword="false"/>。</returns>
        public bool IsOverloaded()
        {
            if (!this.options.Enabled)
                return false;

            DateTime now = this.timeProvider.GetUtcNow().UtcDateTime;
            this.monitor.TryAction(now);
            double txPerSecondCurrently = CalculateTps(this.lastStatistics.TransactionsStarted, this.lastCheckTime, this.statistics.TransactionsStarted, now);
            //以 tx 每秒为tx 使用衰变
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
