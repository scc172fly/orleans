using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于配置部署负载发布的选项。
    /// </summary>
    public class DeploymentLoadPublisherOptions
    {
        /// <summary>
        /// 发布部署统计信息的时间间隔。
        /// </summary>
        public TimeSpan DeploymentLoadPublisherRefreshTime { get; set; } = DEFAULT_DEPLOYMENT_LOAD_PUBLISHER_REFRESH_TIME;

        /// <summary>
        /// <see cref="DeploymentLoadPublisherRefreshTime"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DEPLOYMENT_LOAD_PUBLISHER_REFRESH_TIME = TimeSpan.FromSeconds(1);
    }
}
