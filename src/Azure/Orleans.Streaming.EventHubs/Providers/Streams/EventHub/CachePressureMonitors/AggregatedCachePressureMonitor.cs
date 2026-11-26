using Orleans.Providers.Streams.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 累积缓存压力监测器
    /// </summary>
    public partial class AggregatedCachePressureMonitor : List<ICachePressureMonitor>, ICachePressureMonitor
    {
        private bool isUnderPressure;
        private readonly ILogger logger;
        /// <summary>
        /// 用于报告缓存相关度量的缓存监测器
        /// </summary>
        public ICacheMonitor? CacheMonitor { set; private get; }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public AggregatedCachePressureMonitor(ILogger logger, ICacheMonitor? monitor = null)
        {
            this.isUnderPressure = false;
            this.logger = logger;
            this.CacheMonitor = monitor;
        }

        /// <summary>
        /// 记录这个总密藏缓存监控组中每个监视器的缓存压力
        /// </summary>
        /// <param name="cachePressureContribution"></param>
        public void RecordCachePressureContribution(double cachePressureContribution)
        {
            this.ForEach(monitor =>
            {
                monitor.RecordCachePressureContribution(cachePressureContribution);
            });
        }

        /// <summary>
        /// 添加一个监视器到此汇总缓存监测组
        /// </summary>
        /// <param name="monitor"></param>
        public void AddCachePressureMonitor(ICachePressureMonitor monitor)
        {
            this.Add(monitor);
        }

        /// <summary>
        /// 如果这个总密藏监测组的任何监测员受到压力,那么返回真实
        /// </summary>
        /// <param name="utcNow"></param>
        /// <returns></returns>
        public bool IsUnderPressure(DateTime utcNow)
        {
            bool underPressure = this.Exists(monitor => monitor.IsUnderPressure(utcNow));
            if (this.isUnderPressure != underPressure)
            {
                this.isUnderPressure = underPressure;
                this.CacheMonitor?.TrackCachePressureMonitorStatusChange(this.GetType().Name, this.isUnderPressure, null, null, null);
                if (this.isUnderPressure)
                {
                    LogInfoIngestingMessagesTooFast();
                }
                else
                {
                    LogInfoMessageIngestionIsHealthy();
                }
            }
            return underPressure;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Ingesting messages too fast. Throttling message reading."
        )]
        private partial void LogInfoIngestingMessagesTooFast();

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Message ingestion is healthy."
        )]
        private partial void LogInfoMessageIngestionIsHealthy();
    }
}
