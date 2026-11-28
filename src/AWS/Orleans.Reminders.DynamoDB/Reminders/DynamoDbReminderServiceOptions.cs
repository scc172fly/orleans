namespace Orleans.Configuration
{
    /// <summary>
    /// Amazon DynamoDB 提醒器存储配置。
    /// </summary>
    public class DynamoDBReminderTableOptions
    {
        /// <summary>
        /// 获取或设置连接字符串。
        /// </summary>
        [RedactConnectionString]
        public string ConnectionString { get; set; } = null!;
    }
}
