using Orleans.Runtime;

namespace UnitTests.GrainInterfaces
{
    [GenerateSerializer]
    public record class ReminderState([property: Id(0)] IGrainReminder Reminder)
    {
        [Id(1)] public DateTime? Registered { get; init; } = null;
        [Id(2)] public DateTime? Unregistered { get; init; } = null;
        [Id(3)] public List<DateTime> Fired { get; init; } = new();
        [Id(4)] public List<(DateTime, string)> Log { get; init; } = new();
    }

    public interface IReminderTestGrain2 : IGrainWithGuidKey
    {
        Task<IGrainReminder> StartReminder(string reminderName, TimeSpan? period = null, bool validate = false);
        Task<IGrainReminder> StartReminder(string reminderName, TimeSpan dueTime, TimeSpan period);

        Task StopReminder(string reminderName);
        Task StopReminder(IGrainReminder reminder);

        Task<TimeSpan> GetReminderPeriod(string reminderName);
        Task<(TimeSpan DueTime, TimeSpan Period)> GetReminderDueTimeAndPeriod(string reminderName);
        Task<long> GetCounter(string name);
        Task<IGrainReminder?> GetReminderObject(string reminderName);
        Task<List<IGrainReminder>> GetRemindersList();

        Task EraseReminderTable();

        Task<Dictionary<string, ReminderState>> GetReminderStates();
    }

    // 用于测试不同 Grain 类型的提醒器
    public interface IReminderTestCopyGrain : IGrainWithGuidKey
    {
        Task<IGrainReminder> StartReminder(string reminderName, TimeSpan? period = null, bool validate = false);
        Task StopReminder(string reminderName);

        Task<TimeSpan> GetReminderPeriod(string reminderName);
        Task<long> GetCounter(string name);
    }

    public interface IReminderGrainWrong : IGrainWithIntegerKey
    // 由于此 Grain 未实现 IRemindable，应在运行时收到错误
    // 需要一种方法，在未实现 IRemindable 却尝试注册提醒器时于编译期告知用户
    {
        Task<bool> StartReminder(string reminderName);
    }
}
