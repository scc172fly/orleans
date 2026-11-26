using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="DeploymentBasedQueueBalancer"/>选项。
    /// </summary>
    public class DeploymentBasedQueueBalancerOptions
    {
        /// <summary>
        /// 获取或设定 silo 到期日,即允许 silo 在重新平衡队列前保持活动的时间。
        /// </summary>
        /// <value>下 silo 到期日。</value>
        public TimeSpan SiloMaturityPeriod { get; set; } = DEFAULT_SILO_MATURITY_PERIOD;

        /// <summary>
        /// 默认 silo 到期期。
        /// </summary>
        public static readonly TimeSpan DEFAULT_SILO_MATURITY_PERIOD = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 获取或设置一个值,以显示是否假设静态(固定)集。
        /// </summary>
        public bool IsFixed { get; set; }
    }
}
