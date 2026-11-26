namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 ZooKeeperMembership的选项
    /// </summary>
    public class ZooKeeperClusteringSiloOptions
    {
        /// <summary>
        /// ZooKeeper 存储的连接字符串
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; } = null!;
    }
}
