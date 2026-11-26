using System;
using Microsoft.Extensions.Logging;
using Orleans.Providers.Streams.Common;
using Orleans.Configuration;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 其背压力算法基于平均压力值的缓存压力监视器
    /// 压力超过所有压力
    /// </summary>
    public partial class AveragingCachePressureMonitor : ICachePressureMonitor
    {
        /// <summary>
        /// 用于报告缓存相关度量的缓存监测器
        /// </summary>
        public ICacheMonitor? CacheMonitor { set; private get; }
        private static readonly TimeSpan checkPeriod = TimeSpan.FromSeconds(2);
        private readonly ILogger logger;

        private double accumulatedCachePressure;
        private double cachePressureContributionCount;
        private DateTime nextCheckedTime;
        private bool isUnderPressure;
        private readonly double flowControlThreshold;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public AveragingCachePressureMonitor(ILogger logger, ICacheMonitor? monitor = null)
            : this(EventHubStreamCachePressureOptions.DEFAULT_AVERAGING_CACHE_PRESSURE_MONITORING_THRESHOLD, logger, monitor)
        { }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="flowControlThreshold"></param>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public AveragingCachePressureMonitor(double flowControlThreshold, ILogger logger, ICacheMonitor? monitor = null)
        {
            this.flowControlThreshold = flowControlThreshold;
            this.logger = logger;
            nextCheckedTime = DateTime.MinValue;
            isUnderPressure = false;
            this.CacheMonitor = monitor;
        }

        /// <inheritdoc />
        public void RecordCachePressureContribution(double cachePressureContribution)
        {
            // 体重比健康贡献高三倍。
            // 健康消费者消费比不健康的消费者多,这是对这一事实的粗略补偿。
            double weight = cachePressureContribution < flowControlThreshold ? 1.0 : 3.0;
            accumulatedCachePressure += cachePressureContribution * weight;
            cachePressureContributionCount += weight;
        }

        /// <inheritdoc />
        public bool IsUnderPressure(DateTime utcNow)
        {
            if (nextCheckedTime < utcNow)
            {
                CalculatePressure();
                nextCheckedTime = utcNow + checkPeriod;
            }
            return isUnderPressure;
        }

        private void CalculatePressure()
        {
            // 如果我们没有捐款, 不要改变状态
            if (cachePressureContributionCount < 0.5)
            {
                // 5次支票之后,没有交款,反正要查
                cachePressureContributionCount += 0.1;
                return;
            }

            double pressure = accumulatedCachePressure / cachePressureContributionCount;
            bool wasUnderPressure = isUnderPressure;
            isUnderPressure = pressure > flowControlThreshold;
            // 如果我们改变状态, 日志
            if (isUnderPressure != wasUnderPressure)
            {
                this.CacheMonitor?.TrackCachePressureMonitorStatusChange(this.GetType().Name, isUnderPressure, cachePressureContributionCount, pressure, this.flowControlThreshold);
                if (isUnderPressure)
                {
                    LogDebugIngestingMessagesTooFast(accumulatedCachePressure, cachePressureContributionCount, pressure, flowControlThreshold);
                }
                else
                {
                    LogDebugMessageIngestionIsHealthy(accumulatedCachePressure, cachePressureContributionCount, pressure, flowControlThreshold);
                }
            }
            cachePressureContributionCount = 0.0;
            accumulatedCachePressure = 0.0;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Ingesting messages too fast. Throttling message reading. AccumulatedCachePressure: {AccumulatedCachePressure}, Contributions: {Contributions}, AverageCachePressure: {AverageCachePressure}, Threshold: {FlowControlThreshold}"
        )]
        private partial void LogDebugIngestingMessagesTooFast(double accumulatedCachePressure, double contributions, double averageCachePressure, double flowControlThreshold);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Message ingestion is healthy. AccumulatedCachePressure: {AccumulatedCachePressure}, Contributions: {Contributions}, AverageCachePressure: {AverageCachePressure}, Threshold: {FlowControlThreshold}"
        )]
        private partial void LogDebugMessageIngestionIsHealthy(double accumulatedCachePressure, double contributions, double averageCachePressure, double flowControlThreshold);
    }
}
