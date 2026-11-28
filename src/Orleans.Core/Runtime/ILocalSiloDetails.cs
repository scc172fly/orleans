namespace Orleans.Runtime
{
    /// <summary>
    /// 本地 Silo 的详细信息。
    /// </summary>
    public interface ILocalSiloDetails
    {
        /// <summary>
        /// 获取此 Silo 的名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取集群标识。Orleans 2.0 之前称为 DeploymentId。
        /// </summary>
        string ClusterId { get; }

        /// <summary>
        /// 获取此 Silo 的主机名。
        /// </summary>
        /// <remarks>
        /// 此值等于 <see cref="System.Net.Dns.GetHostName()"/>。
        /// </remarks>
        string DnsHostName { get; }

        /// <summary>
        /// 获取此 Silo 用于 Silo 间通信的终结点地址。
        /// </summary>
        SiloAddress SiloAddress { get; }

        /// <summary>
        /// 获取此 Silo 的网关代理终结点地址。
        /// </summary>
        SiloAddress GatewayAddress { get; }
    }
}
