using Orleans.Reminders.DynamoDB;

namespace Orleans.Configuration
{
    /// <summary>
    /// 亚马逊 DynamoDB 提醒存储配置 。
    /// </summary>
    public class DynamoDBReminderStorageOptions : DynamoDBClientOptions
    {
        /// <summary>
        /// DynamoDB 储存读能力单位
        /// </summary>
        public int ReadCapacityUnits { get; set; } = DynamoDBStorage.DefaultReadCapacityUnits;

        /// <summary>
        /// DynamoDB 储存的写能力单位
        /// </summary>
        public int WriteCapacityUnits { get; set; } = DynamoDBStorage.DefaultWriteCapacityUnits;

        /// <summary>
        /// 表中已提供投入的用途
        /// </summary>
        public bool UseProvisionedThroughput { get; set; } = true;

        /// <summary>
        /// 如果表格不存在创建表格
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 如果有更新表格,则更新列表
        /// </summary>
        public bool UpdateIfExists { get; set; } = true;

        /// <summary>
        /// DynamoDB 表格名称。
        /// 默认值为 'OrleansReminders' 。
        /// </summary>
        public string TableName { get; set; } = "OrleansReminders";
    }
}
