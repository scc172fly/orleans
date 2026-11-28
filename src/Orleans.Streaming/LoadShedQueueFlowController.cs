
using System;
using Orleans.Configuration;
using Orleans.Statistics;

namespace Orleans.Streams
{
    /// <summary>
    /// 由 Silo 负载削减触发的流量控制。
    /// 这是全有或全无的触发机制，将请求 <see cref="int.MaxValue"/> 或 <c>0</c>。
    /// </summary>
    public class LoadShedQueueFlowController : IQueueFlowController
    {
        private readonly LoadSheddingOptions options;
        private readonly double loadSheddingLimit;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;

        /// <summary>
        /// 创建一个流量控制器，当 CPU 使用率达到集群负载削减限制的指定百分比时触发。
        /// 其目的是在 Silo 开始削减负载之前降低队列读取速率。
        /// 注意：仅在启用负载削减时触发。
        /// </summary>
        /// <param name="options">Silo 统计选项。</param>
        /// <param name="percentOfSiloSheddingLimit">触发降低队列读取速率的负载削减限制百分比。</param>
        /// <param name="environmentStatisticsProvider">Silo 环境统计信息。</param>
        /// <returns>流量控制器。</returns>
        public static IQueueFlowController CreateAsPercentOfLoadSheddingLimit(LoadSheddingOptions options, IEnvironmentStatisticsProvider environmentStatisticsProvider, int percentOfSiloSheddingLimit = LoadSheddingOptions.DefaultCpuThreshold)
        {
            if (percentOfSiloSheddingLimit < 0.0 || percentOfSiloSheddingLimit > 100.0) throw new ArgumentOutOfRangeException(nameof(percentOfSiloSheddingLimit), "Percent value must be between 0-100");
            // 在 Silo 达到负载削减限制之前开始削减负载。
            return new LoadShedQueueFlowController((int)(options.CpuThreshold * (percentOfSiloSheddingLimit / 100.0)), options, environmentStatisticsProvider);
        }

        /// <summary>
        /// 创建一个流量控制器，当 CPU 使用率达到指定限制时触发。
        /// 注意：仅在启用负载削减时触发。
        /// </summary>
        /// <param name="loadSheddingLimit">触发降低队列读取速率的 CPU 使用率百分比。</param>
        /// <param name="options">Silo 统计选项。</param>
        /// <param name="environmentStatisticsProvider">Silo 环境统计信息。</param>
        /// <returns>流量控制器。</returns>
        public static IQueueFlowController CreateAsPercentageOfCPU(int loadSheddingLimit, LoadSheddingOptions options, IEnvironmentStatisticsProvider environmentStatisticsProvider)
        {
            if (loadSheddingLimit < 0 || loadSheddingLimit > 100) throw new ArgumentOutOfRangeException(nameof(loadSheddingLimit), "Value must be between 0-100");
            return new LoadShedQueueFlowController(loadSheddingLimit, options, environmentStatisticsProvider);
        }

        private LoadShedQueueFlowController(int loadSheddingLimit, LoadSheddingOptions options, IEnvironmentStatisticsProvider environmentStatisticsProvider)
        {
            this.options = options;
            if (loadSheddingLimit < 0 || loadSheddingLimit > 100) throw new ArgumentOutOfRangeException(nameof(loadSheddingLimit), "Value must be between 0-100");
            this.loadSheddingLimit = loadSheddingLimit != 0 ? loadSheddingLimit : int.MaxValue;
            this.environmentStatisticsProvider = environmentStatisticsProvider;
        }

        /// <inheritdoc/>
        public int GetMaxAddCount()
        {
            return options.LoadSheddingEnabled && environmentStatisticsProvider.GetEnvironmentStatistics().FilteredCpuUsagePercentage > loadSheddingLimit ? 0 : int.MaxValue;
        }
    }
}
