using System;
using System.Collections.Generic;
using Orleans.Hosting;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于配置静态网关列表的选项。
    /// </summary>
    /// <remarks>>
    /// 有关详细信息，请参阅 <see cref="ClientBuilderExtensions.UseStaticClustering(IClientBuilder, System.Net.IPEndPoint[])"/>。
    /// </remarks>
    public class StaticGatewayListProviderOptions
    {
        /// <summary>
        /// 获取或设置网关地址列表。
        /// </summary>
        public List<Uri> Gateways { get; set; } = new List<Uri>();
    }
}
