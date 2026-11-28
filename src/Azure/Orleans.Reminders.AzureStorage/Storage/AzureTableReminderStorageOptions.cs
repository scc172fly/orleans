namespace Orleans.Reminders.AzureStorage
{
    /// <summary>基于 Azure Table 的提醒器表选项。</summary>
    public class AzureTableReminderStorageOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// Azure Storage 表名。
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

        /// <summary>
        /// 用于提醒器的默认 Azure Table Storage 表名。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "OrleansReminders";
    }

    /// <summary>
    /// <see cref="AzureTableReminderStorageOptions"/> 的配置验证器。
    /// </summary>
    public class AzureTableReminderStorageOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableReminderStorageOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableReminderStorageOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
        public AzureTableReminderStorageOptionsValidator(AzureTableReminderStorageOptions options, string name) : base(options, name)
        {
        }
    }
}
