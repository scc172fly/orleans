namespace Orleans.Configuration
{
    /// <summary>
    /// 配置客户端发现网关时对 ZooKeeper 的访问。
    /// </summary>
    public class ZooKeeperGatewayListProviderOptions
    {
        /// <summary>
        /// ZooKeeper 存储连接字符串。
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; } = null!;
    }
}
