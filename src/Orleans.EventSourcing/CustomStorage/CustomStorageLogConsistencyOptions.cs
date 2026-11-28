namespace Orleans.Configuration
{
    /// <summary>
    /// 配置自定义存储的日志一致性提供程序。
    /// </summary>
    public class CustomStorageLogConsistencyOptions
    {
        /// <summary>
        /// 获取或设置传递给每个自定义存储适配器的集群标识符。
        /// 自定义存储适配器接受来自所有集群的提交。
        /// </summary>
        public string? PrimaryCluster { get; set; }
    }
}
