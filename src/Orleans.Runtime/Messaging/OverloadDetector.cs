using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Messaging;
using Orleans.Statistics;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// 确定进程是否过载。
    /// </summary>
    public interface IOverloadDetector
    {
        /// <summary>
        /// 若进程过载则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        bool IsOverloaded { get; }
    }

    /// <summary>
    /// 确定进程是否过载。
    /// </summary>
    internal class OverloadDetector : IOverloadDetector
    {
        private const int RefreshIntervalMilliseconds = 1_000;
        private readonly IEnvironmentStatisticsProvider _environmentStatisticsProvider;
        private readonly LoadSheddingOptions _options;
        private CoarseStopwatch _refreshStopwatch;
        private bool? _isOverloaded;

        public OverloadDetector(IEnvironmentStatisticsProvider environmentStatisticsProvider, IOptions<LoadSheddingOptions> loadSheddingOptions)
        {
            _environmentStatisticsProvider = environmentStatisticsProvider;
            _options = loadSheddingOptions.Value;

            Enabled = _options.LoadSheddingEnabled;

            _refreshStopwatch = CoarseStopwatch.StartNew();
        }

        /// <summary>
        /// 获取或设置指示是否启用过载检测的值。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 若进程过载则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        public bool IsOverloaded
        {
            get
            {
                if (!Enabled)
                {
                    return false;
                }

                if (!_isOverloaded.HasValue || _refreshStopwatch.ElapsedMilliseconds >= RefreshIntervalMilliseconds)
                {
                    var stats = _environmentStatisticsProvider.GetEnvironmentStatistics();
                    _isOverloaded = OverloadDetectionLogic.IsOverloaded(ref stats, _options);
                    _refreshStopwatch.Restart();
                }

                return _isOverloaded.Value;
            }
        }

        // 仅为测试目的公开。
        internal void ForceRefresh()
        {
            _isOverloaded = null;
        }
    }
}
