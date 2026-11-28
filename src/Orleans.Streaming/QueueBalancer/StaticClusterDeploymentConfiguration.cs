using System.Collections.Generic;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 从 Orleans 集群配置读取信息的部署配置。
    /// </summary>
    public class StaticClusterDeploymentOptions : IDeploymentConfiguration
    {
        /// <summary>
        /// 获取或设置 Silo 名称。
        /// </summary>
        /// <value>Silo 名称。</value>
        public IList<string> SiloNames { get; set; } = new List<string>();

        /// <inheritdoc/>
        IList<string> IDeploymentConfiguration.GetAllSiloNames()
        {
            return this.SiloNames;
        }
    }
}
