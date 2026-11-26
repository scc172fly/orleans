using Orleans.Hosting;

namespace Orleans;

internal static class ReminderOptionsDefaults
{
    /// <summary>
    /// 登记提醒的最低期限.我们想执行一个较低的约束 <see cref="ReminderOptions.MinimumReminderPeriod"/>。
    /// </summary>
    /// <remarks>增加这一时期,提醒时间应该少一些... 我们只需要用两秒钟来缩短单位测试的运行时间</remarks>
    public const uint MinimumReminderPeriodMinutes = 1;

    /// <summary>
    /// 刷新本地提醒列表以反映全球提醒表的间隔时间(分钟),每 <see cref="ReminderOptions.RefreshReminderListPeriod"/>。
    /// </summary>
    public const uint RefreshReminderListPeriodMinutes = 5;

    /// <summary>
    /// 下次选中时, 提醒被装入内存的时间( 分钟) 。
    /// </summary>
    public const uint ReminderLoadingWindowMinutes = 2 * RefreshReminderListPeriodMinutes;

    /// <summary>
    /// 尝试启动放弃提醒的最长时间(分钟) <see cref="ReminderOptions.InitializationTimeout"/>。
    /// </summary>
    public const uint InitializationTimeoutMinutes = 5;
}
