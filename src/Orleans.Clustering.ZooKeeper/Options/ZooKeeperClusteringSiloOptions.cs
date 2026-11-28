namespace Orleans.Configuration
{
    /// <summary>
    /// 用于配置 ZooKeeperMembership 的选项。
    /// </summary>
    public class ZooKeeperClusteringSiloOptions
    {
        /// <summary>
        /// ZooKeeper 存储连接字符串。
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; } = null!;
    }
}
