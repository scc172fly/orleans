using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置客户如何与网关端点互动的选项。
    /// </summary>
    public class GatewayOptions
    {
        /// <summary>
        /// 获取或设置更新活动网关列表之间的时间间隔 。
        /// </summary>
        /// <value>活动网关列表将默认每分钟更新一次。</value>
        public TimeSpan GatewayListRefreshPeriod { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 默认首选网关索引,。 值 - 1 表示不优先
        /// </summary>
        public const int DEFAULT_PREFERED_GATEWAY_INDEX = -1;

        /// <summary>
        /// 获取或设置活动网关列表中首选网关的索引。
        /// </summary>
        /// <remarks>设置此值为默认值, <c>-1</c>,以禁用该功能。</remarks>
        /// <value>默认情况下没有首选网关。</value>
        public int PreferredGatewayIndex { get; set; } = DEFAULT_PREFERED_GATEWAY_INDEX;
    }
}
