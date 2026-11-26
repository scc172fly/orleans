namespace Orleans.Reminders.AzureStorage
{
    /// <summary>供 Azure 表格为基础的提醒表。</summary>
    public class AzureTableReminderStorageOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// Azure 存储的表格名称
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

        /// <summary>
        /// 用于提醒的默认 Azure 表格存储表名称。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "OrleansReminders";
    }

    /// <summary>
    /// <see cref="AzureTableReminderStorageOptions"/>的配置验证符 。
    /// </summary>
    public class AzureTableReminderStorageOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableReminderStorageOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableReminderStorageOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
        public AzureTableReminderStorageOptionsValidator(AzureTableReminderStorageOptions options, string name) : base(options, name)
        {
        }
    }
}
