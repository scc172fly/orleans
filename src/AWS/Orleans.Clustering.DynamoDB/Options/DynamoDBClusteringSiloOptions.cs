namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Silo 用于集群成员管理的 DynamoDB 连接。
    /// </summary>
    public class DynamoDBClusteringSiloOptions
    {
        /// <summary>
        /// DynamoDB 存储连接字符串。
        /// </summary>
        [RedactConnectionString]
        public string ConnectionString { get; set; } = null!;
    }
}
