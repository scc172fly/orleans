using System.Net;

namespace Orleans.Configuration
{
    /// <summary>配置开发组合组合选项</summary>
    public class DevelopmentClusterMembershipOptions
    {
        /// <summary>
        /// 获取或设置种子节点以找到成员资格制度 grain。
        /// </summary>
        public IPEndPoint? PrimarySiloEndpoint { get; set; }
    }
}
