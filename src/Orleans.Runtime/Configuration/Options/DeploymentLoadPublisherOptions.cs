using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置部署负荷发布选项
    /// </summary>
    public class DeploymentLoadPublisherOptions
    {
        /// <summary>
        /// 公布部署统计数据的间隔期。
        /// </summary>
        public TimeSpan DeploymentLoadPublisherRefreshTime { get; set; } = DEFAULT_DEPLOYMENT_LOAD_PUBLISHER_REFRESH_TIME;

        /// <summary>
        /// <see cref="DeploymentLoadPublisherRefreshTime"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DEPLOYMENT_LOAD_PUBLISHER_REFRESH_TIME = TimeSpan.FromSeconds(1);
    }
}
