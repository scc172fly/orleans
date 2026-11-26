using Orleans.Runtime;

namespace Orleans.Reminders.TestKit.Tests;

/// <summary>
/// 最小提醒 grain 所用指南使用 TestKit 集束集束体集成试验。
/// </summary>
public interface IReminderTestKitGrain : IGrainWithGuidKey
{
    /// <summary>登记或更新提醒通知并归还其姓名。</summary>
    Task<string> RegisterReminderAsync(string reminderName, TimeSpan dueTime, TimeSpan period);

    /// <summary>提醒和报告是否找到</summary>
    Task<bool> UnregisterReminderAsync(string reminderName);

    /// <summary>返回当前登记为此 grain的提醒通知的名称。</summary>
    Task<List<string>> GetReminderNamesAsync();
}

/// <inheritdoc cref="IReminderTestKitGrain" />
public sealed class ReminderTestKitGrain : Grain, IReminderTestKitGrain, IRemindable
{
    /// <inheritdoc />
    public async Task<string> RegisterReminderAsync(string reminderName, TimeSpan dueTime, TimeSpan period)
    {
        var reminder = await this.RegisterOrUpdateReminder(reminderName, dueTime, period);
        return reminder.ReminderName;
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterReminderAsync(string reminderName)
    {
        var reminder = await this.GetReminder(reminderName);
        if (reminder is null)
        {
            return false;
        }

        await this.UnregisterReminder(reminder);
        return true;
    }

    /// <inheritdoc />
    public async Task<List<string>> GetReminderNamesAsync()
    {
        var reminders = await this.GetReminders();
        return [.. reminders.Select(reminder => reminder.ReminderName)];
    }

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) => Task.CompletedTask;
}
