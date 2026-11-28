using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="DeploymentBasedQueueBalancer"/> 的选项。
    /// </summary>
    public class DeploymentBasedQueueBalancerOptions
    {
        /// <summary>
        /// 获取或设置 Silo 成熟期，即在对队列进行再均衡之前，等待 Silo 持续保持活动的时长。
        /// </summary>
        /// <value>Silo 成熟期。</value>
        public TimeSpan SiloMaturityPeriod { get; set; } = DEFAULT_SILO_MATURITY_PERIOD;

        /// <summary>
        /// 默认 Silo 成熟期。
        /// </summary>
        public static readonly TimeSpan DEFAULT_SILO_MATURITY_PERIOD = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 获取或设置一个值，指示是否假定集群是静态（固定）的。
        /// </summary>
        public bool IsFixed { get; set; }
    }
}
