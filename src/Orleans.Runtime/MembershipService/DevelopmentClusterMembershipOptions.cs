using System.Net;

namespace Orleans.Configuration
{
    /// <summary>配置开发环境集群选项。</summary>
    public class DevelopmentClusterMembershipOptions
    {
        /// <summary>
        /// 获取或设置用于查找成员资格系统 Grain 的种子节点。
        /// </summary>
        public IPEndPoint? PrimarySiloEndpoint { get; set; }
    }
}
