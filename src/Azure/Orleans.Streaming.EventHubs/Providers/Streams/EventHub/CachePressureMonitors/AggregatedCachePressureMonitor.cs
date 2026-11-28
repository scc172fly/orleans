using Orleans.Providers.Streams.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 聚合缓存压力监视器。
    /// </summary>
    public partial class AggregatedCachePressureMonitor : List<ICachePressureMonitor>, ICachePressureMonitor
    {
        private bool isUnderPressure;
        private readonly ILogger logger;
        /// <summary>
        /// 用于报告缓存相关指标的缓存监视器。
        /// </summary>
        public ICacheMonitor? CacheMonitor { set; private get; }

        /// <summary>
        /// 构造函数。
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
        /// 向此聚合缓存监视器组中的每个监视器记录缓存压力。
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
        /// 将一个监视器添加到此聚合缓存监视器组。
        /// </summary>
        /// <param name="monitor"></param>
        public void AddCachePressureMonitor(ICachePressureMonitor monitor)
        {
            this.Add(monitor);
        }

        /// <summary>
        /// 如果聚合缓存监视器组中任一监视器处于压力状态，则返回 true。
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
