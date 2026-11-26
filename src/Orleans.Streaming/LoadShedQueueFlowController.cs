
using System;
using Orleans.Configuration;
using Orleans.Statistics;

namespace Orleans.Streams
{
    /// <summary>
    /// 由 silo 卸载叠置引发的流程控制。
    /// 这是要求 <see cref="int.MaxValue"/>或 <c>0</c>的全无触发。
    /// </summary>
    public class LoadShedQueueFlowController : IQueueFlowController
    {
        private readonly LoadSheddingOptions options;
        private readonly double loadSheddingLimit;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;

        /// <summary>
        /// 当 CPU 达到组群负荷递减限制的一定百分比时, 创建流量控制器 。
        /// 这样做的目的是在 silo 卸载前降低队列读取率。
        /// 注: 仅在启用加载切换时触发。
        /// </summary>
        /// <param name="options"> silo 统计备选办法。</param>
        /// <param name="percentOfSiloSheddingLimit">导致排队阅读率降低的负载舱限制百分比。</param>
        /// <param name="environmentStatisticsProvider"> silo 环境统计。</param>
        /// <returns>流控器。</returns>
        public static IQueueFlowController CreateAsPercentOfLoadSheddingLimit(LoadSheddingOptions options, IEnvironmentStatisticsProvider environmentStatisticsProvider, int percentOfSiloSheddingLimit = LoadSheddingOptions.DefaultCpuThreshold)
        {
            if (percentOfSiloSheddingLimit < 0.0 || percentOfSiloSheddingLimit > 100.0) throw new ArgumentOutOfRangeException(nameof(percentOfSiloSheddingLimit), "Percent value must be between 0-100");
            // 切换时间在 silo 到裁剪限值之前开始。
            return new LoadShedQueueFlowController((int)(options.CpuThreshold * (percentOfSiloSheddingLimit / 100.0)), options, environmentStatisticsProvider);
        }

        /// <summary>
        /// 创建当 CPU 达到指定限制时触发的流程控制器。
        /// 注: 仅在启用加载切换时触发。
        /// </summary>
        /// <param name="loadSheddingLimit">触发队列减读率的CPU百分比</param>
        /// <param name="options"> silo 统计备选办法。</param>
        /// <param name="environmentStatisticsProvider"> silo 环境统计。</param>
        /// <returns>流控器。</returns>
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
