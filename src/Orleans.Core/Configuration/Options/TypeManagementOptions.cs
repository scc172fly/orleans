using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 现时升级所需的类型管理设置。
    /// </summary>
    public class TypeManagementOptions
    {
        /// <summary>
        /// 更新组群 grain 接口地图的秒数
        /// </summary>
        public TimeSpan TypeMapRefreshInterval { get; set; } = DEFAULT_REFRESH_CLUSTER_INTERFACEMAP_TIME;

        /// <summary>
        /// 组群 grain 接口地图刷新之间的默认间隔。
        /// </summary>
        public static readonly TimeSpan DEFAULT_REFRESH_CLUSTER_INTERFACEMAP_TIME = TimeSpan.FromMinutes(1);
    }
}
