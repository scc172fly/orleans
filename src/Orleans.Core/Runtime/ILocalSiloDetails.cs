namespace Orleans.Runtime
{
    /// <summary>
    /// 本地 silo的详情 。
    /// </summary>
    public interface ILocalSiloDetails
    {
        /// <summary>
        /// 获取此 silo的名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取集组身份。 这以前被称为 DeploymentId 之前 Orleans 2.0 名。
        /// </summary>
        string ClusterId { get; }

        /// <summary>
        /// 获取此 silo主机名 。
        /// </summary>
        /// <remarks>
        /// 这等于 <see cref="System.Net.Dns.GetHostName()"/>。
        /// </remarks>
        string DnsHostName { get; }

        /// <summary>
        /// 获取 silo的间silo 端点的地址。
        /// </summary>
        SiloAddress SiloAddress { get; }

        /// <summary>
        /// 获取此 silo网关的网关代理端点的地址。
        /// </summary>
        SiloAddress GatewayAddress { get; }
    }
}
