namespace Orleans.Configuration
{
    /// <summary>
    /// 亚马逊 DynamoDB 提醒存储配置 。
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
