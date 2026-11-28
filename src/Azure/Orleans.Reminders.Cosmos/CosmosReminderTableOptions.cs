namespace Orleans.Reminders.Cosmos;

/// <summary>
/// Azure Cosmos DB 提醒器存储选项。
/// </summary>
public class CosmosReminderTableOptions : CosmosOptions
{
    private const string ORLEANS_REMINDERS_CONTAINER = "OrleansReminders";

    /// <summary>
    /// 初始化新的 <see cref="CosmosReminderTableOptions"/> 实例。
    /// </summary>
    public CosmosReminderTableOptions()
    {
        ContainerName = ORLEANS_REMINDERS_CONTAINER;
    }
}
