namespace Orleans.Configuration
{
    /// <summary>
    /// 配置自定义存储日志一致性提供者 。
    /// </summary>
    public class CustomStorageLogConsistencyOptions
    {
        /// <summary>
        /// 获取或设置传递到每个自定义存储适应器的集束识别码。
        /// 自定义存储适应器接受每个组群的提交文件。
        /// </summary>
        public string? PrimaryCluster { get; set; }
    }
}
