using Orleans.Hosting;

namespace Orleans;

internal static class ReminderOptionsDefaults
{
    /// <summary>
    /// 注册提醒器时允许的最短周期；我们希望强制执行下限 <see cref="ReminderOptions.MinimumReminderPeriod"/>。
    /// </summary>
    /// <remarks>实际使用时应增大此周期，提醒器的触发频率应更低；这里使用 2 秒仅为了缩短单元测试的运行时间。</remarks>
    public const uint MinimumReminderPeriodMinutes = 1;

    /// <summary>
    /// 每隔 <see cref="ReminderOptions.RefreshReminderListPeriod"/>（以分钟计）刷新本地提醒器列表，使其反映全局提醒器表。
    /// </summary>
    public const uint RefreshReminderListPeriodMinutes = 5;

    /// <summary>
    /// 在提醒器下次触发之前将其加载到内存的提前时间（以分钟计）。
    /// </summary>
    public const uint ReminderLoadingWindowMinutes = 2 * RefreshReminderListPeriodMinutes;

    /// <summary>
    /// 放弃初始化提醒器之前允许尝试的最长时间（以分钟计）<see cref="ReminderOptions.InitializationTimeout"/>。
    /// </summary>
    public const uint InitializationTimeoutMinutes = 5;
}
