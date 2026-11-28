using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于就地升级的类型管理设置。
    /// </summary>
    public class TypeManagementOptions
    {
        /// <summary>
        /// 刷新集群 Grain 接口映射的间隔秒数。
        /// </summary>
        public TimeSpan TypeMapRefreshInterval { get; set; } = DEFAULT_REFRESH_CLUSTER_INTERFACEMAP_TIME;

        /// <summary>
        /// 刷新集群 Grain 接口映射的默认时间间隔。
        /// </summary>
        public static readonly TimeSpan DEFAULT_REFRESH_CLUSTER_INTERFACEMAP_TIME = TimeSpan.FromMinutes(1);
    }
}
