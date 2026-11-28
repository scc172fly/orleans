using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于配置客户端与网关终结点交互方式的选项。
    /// </summary>
    public class GatewayOptions
    {
        /// <summary>
        /// 获取或设置刷新活动网关列表的时间间隔。
        /// </summary>
        /// <value>默认每分钟刷新一次活动网关列表。</value>
        public TimeSpan GatewayListRefreshPeriod { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 默认首选网关索引。值为 -1 表示不偏好任何网关。
        /// </summary>
        public const int DEFAULT_PREFERED_GATEWAY_INDEX = -1;

        /// <summary>
        /// 获取或设置首选网关在活动网关列表中的索引。
        /// </summary>
        /// <remarks>将此值设置为默认值 <c>-1</c> 即可禁用此功能。</remarks>
        /// <value>默认没有首选网关。</value>
        public int PreferredGatewayIndex { get; set; } = DEFAULT_PREFERED_GATEWAY_INDEX;
    }
}
