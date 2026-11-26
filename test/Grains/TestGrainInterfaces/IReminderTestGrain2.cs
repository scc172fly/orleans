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

    // 用于测试不同类型 grain 的提醒通知
    public interface IReminderTestCopyGrain : IGrainWithGuidKey
    {
        Task<IGrainReminder> StartReminder(string reminderName, TimeSpan? period = null, bool validate = false);
        Task StopReminder(string reminderName);

        Task<TimeSpan> GetReminderPeriod(string reminderName);
        Task<long> GetCounter(string name);
    }

    public interface IReminderGrainWrong : IGrainWithIntegerKey
    // 由于 grain 不执行 IRemind , 我们运行时应该有一个错误
    // 需要一种方法,让用户在编译时间知道 IRemindable 是否执行并尝试登记提醒
    {
        Task<bool> StartReminder(string reminderName);
    }
}
