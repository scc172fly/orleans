namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 ZooKeeper 访问客户端网关发现权限。
    /// </summary>
    public class ZooKeeperGatewayListProviderOptions
    {
        /// <summary>
        /// ZooKeeper 存储的连接字符串
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; } = null!;
    }
}
