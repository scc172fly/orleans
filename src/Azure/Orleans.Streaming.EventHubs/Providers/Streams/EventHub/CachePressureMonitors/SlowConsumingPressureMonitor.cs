using Orleans.Providers.Streams.Common;
using System;
using Microsoft.Extensions.Logging;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 压力监视器,它有利于缓存中慢速消费者
    /// </summary>
    public partial class SlowConsumingPressureMonitor : ICachePressureMonitor
    {
        /// <summary>
        /// DefaultPressureWindowSize
        /// </summary>
        public static TimeSpan DefaultPressureWindowSize = TimeSpan.FromMinutes(1);
        /// <summary>
        /// 用于报告缓存相关度量的缓存监测器
        /// </summary>
        public ICacheMonitor? CacheMonitor { set; private get; }
        /// <summary>
        /// 默认流量控制阈值
        /// </summary>
        public const double DefaultFlowControlThreshold = 0.5;

        /// <summary>
        /// PressureWindowSize
        /// </summary>
        public TimeSpan PressureWindowSize { get; set; }
        /// <summary>
        /// FlowControlThreshold
        /// </summary>
        public double FlowControlThreshold { get; set; }

        private readonly ILogger logger;
        private double biggestPressureInCurrentWindow;
        private DateTime nextCheckedTime;
        private bool wasUnderPressure;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public SlowConsumingPressureMonitor(ILogger logger, ICacheMonitor? monitor = null)
            : this(DefaultFlowControlThreshold, DefaultPressureWindowSize, logger, monitor)
        { }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="pressureWindowSize"></param>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public SlowConsumingPressureMonitor(TimeSpan pressureWindowSize, ILogger logger, ICacheMonitor? monitor = null)
            : this(DefaultFlowControlThreshold, pressureWindowSize, logger, monitor)
        {
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="flowControlThreshold"></param>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public SlowConsumingPressureMonitor(double flowControlThreshold, ILogger logger, ICacheMonitor? monitor = null)
            : this(flowControlThreshold, DefaultPressureWindowSize, logger, monitor)
        {
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="flowControlThreshold"></param>
        /// <param name="pressureWindowSzie"></param>
        /// <param name="logger"></param>
        /// <param name="monitor"></param>
        public SlowConsumingPressureMonitor(double flowControlThreshold, TimeSpan pressureWindowSzie, ILogger logger, ICacheMonitor? monitor = null)
        {
            this.FlowControlThreshold = flowControlThreshold;
            this.logger = logger;
            this.nextCheckedTime = DateTime.MinValue;
            this.biggestPressureInCurrentWindow = 0;
            this.wasUnderPressure = false;
            this.CacheMonitor = monitor;
            this.PressureWindowSize = pressureWindowSzie;
        }

        /// <inheritdoc />
        public void RecordCachePressureContribution(double cachePressureContribution)
        {
            if (cachePressureContribution > this.biggestPressureInCurrentWindow)
                biggestPressureInCurrentWindow = cachePressureContribution;
        }

        /// <inheritdoc />
        public bool IsUnderPressure(DateTime utcNow)
        {
            //如果当期的压力贡献大于 flowControlThreshold
            //我们看到藏匿处的压力很大
            bool underPressure = this.biggestPressureInCurrentWindow > this.FlowControlThreshold;

            if (underPressure && !this.wasUnderPressure)
            {
                //如果在压力下,请扩展 nextCheckedTime,确保 wasUnderPressure 对整个窗口是真实的
                this.wasUnderPressure = underPressure;
                this.nextCheckedTime = utcNow + this.PressureWindowSize;
                this.CacheMonitor?.TrackCachePressureMonitorStatusChange(this.GetType().Name, underPressure, null, biggestPressureInCurrentWindow, this.FlowControlThreshold);
                LogDebugIngestingMessagesTooFast(biggestPressureInCurrentWindow, FlowControlThreshold);
                this.biggestPressureInCurrentWindow = 0;
            }

            if (this.nextCheckedTime < utcNow)
            {
                //每个检查期结束时重置 biggestPressureInCurrentPeriod
                this.nextCheckedTime = utcNow + this.PressureWindowSize;
                this.biggestPressureInCurrentWindow = 0;
                //如果窗口末端有压力清除,则对日志进行记录。
                if (this.wasUnderPressure && !underPressure)
                {
                    this.CacheMonitor?.TrackCachePressureMonitorStatusChange(this.GetType().Name, underPressure, null, biggestPressureInCurrentWindow, this.FlowControlThreshold);
                    LogDebugMessageIngestionIsHealthy(biggestPressureInCurrentWindow, FlowControlThreshold);
                }
                this.wasUnderPressure = underPressure;
            }

            return this.wasUnderPressure;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Ingesting messages too fast. Throttling message reading. BiggestPressureInCurrentPeriod: {BiggestPressureInCurrentWindow}, Threshold: {FlowControlThreshold}"
        )]
        private partial void LogDebugIngestingMessagesTooFast(double biggestPressureInCurrentWindow, double flowControlThreshold);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Message ingestion is healthy. BiggestPressureInCurrentPeriod: {BiggestPressureInCurrentWindow}, Threshold: {FlowControlThreshold}"
        )]
        private partial void LogDebugMessageIngestionIsHealthy(double biggestPressureInCurrentWindow, double flowControlThreshold);
    }
}
