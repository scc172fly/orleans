using System;
using System.Collections.Generic;
using Orleans.Hosting;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置网关静态列表的选项 。
    /// </summary>
    /// <remarks>>
    /// 更多信息见 <see cref="ClientBuilderExtensions.UseStaticClustering(IClientBuilder, System.Net.IPEndPoint[])"/> 。
    /// </remarks>
    public class StaticGatewayListProviderOptions
    {
        /// <summary>
        /// 获取或设置网关地址列表。
        /// </summary>
        public List<Uri> Gateways { get; set; } = new List<Uri>();
    }
}
