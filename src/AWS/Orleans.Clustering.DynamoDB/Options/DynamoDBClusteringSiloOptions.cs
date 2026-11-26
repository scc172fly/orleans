namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 DynamoDB 用于 silo 组群。
    /// </summary>
    public class DynamoDBClusteringSiloOptions
    {
        /// <summary>
        /// DynamoDB 存储的连接字符串
        /// </summary>
        [RedactConnectionString]
        public string ConnectionString { get; set; } = null!;
    }
}
