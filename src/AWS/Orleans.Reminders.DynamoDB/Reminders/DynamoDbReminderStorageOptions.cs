using Orleans.Reminders.DynamoDB;

namespace Orleans.Configuration
{
    /// <summary>
    /// Amazon DynamoDB 提醒器存储配置。
    /// </summary>
    public class DynamoDBReminderStorageOptions : DynamoDBClientOptions
    {
        /// <summary>
        /// DynamoDB 存储的读取容量单位。
        /// </summary>
        public int ReadCapacityUnits { get; set; } = DynamoDBStorage.DefaultReadCapacityUnits;

        /// <summary>
        /// DynamoDB 存储的写入容量单位。
        /// </summary>
        public int WriteCapacityUnits { get; set; } = DynamoDBStorage.DefaultWriteCapacityUnits;

        /// <summary>
        /// 对表使用预置吞吐量。
        /// </summary>
        public bool UseProvisionedThroughput { get; set; } = true;

        /// <summary>
        /// 如果表不存在，则创建该表。
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 如果表存在，则更新该表。
        /// </summary>
        public bool UpdateIfExists { get; set; } = true;

        /// <summary>
        /// DynamoDB 表名称。
        /// 默认值为 OrleansReminders。
        /// </summary>
        public string TableName { get; set; } = "OrleansReminders";
    }
}
