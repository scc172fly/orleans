using System.Collections.Generic;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 部署配置,原自 orleans 群组组合
    /// </summary>
    public class StaticClusterDeploymentOptions : IDeploymentConfiguration
    {
        /// <summary>
        /// 获取或设置 silo 名称。
        /// </summary>
        /// <value> silo 名称。</value>
        public IList<string> SiloNames { get; set; } = new List<string>();

        /// <inheritdoc/>
        IList<string> IDeploymentConfiguration.GetAllSiloNames()
        {
            return this.SiloNames;
        }
    }
}
