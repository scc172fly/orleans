using Orleans.Runtime;

namespace Orleans.Reminders.TestKit.Tests;

/// <summary>
/// TestKit 集群集成测试使用的最简可接收提醒的 Grain。
/// </summary>
public interface IReminderTestKitGrain : IGrainWithGuidKey
{
    /// <summary>注册或更新提醒器并返回其名称。</summary>
    Task<string> RegisterReminderAsync(string reminderName, TimeSpan dueTime, TimeSpan period);

    /// <summary>注销提醒器并报告是否找到该提醒器。</summary>
    Task<bool> UnregisterReminderAsync(string reminderName);

    /// <summary>返回当前为此 Grain 注册的提醒器名称。</summary>
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
