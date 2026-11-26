using Orleans.TestingHost;

namespace Orleans.Testing.Reminders;

/// <summary>
/// 能够对 <see cref="InProcessTestClusterBuilder"/>进行确定性提醒-测试行为的扩展方法。
/// </summary>
public static class ReminderTestClusterBuilderExtensions
{
    /// <summary>
    /// 在所提供的 <see cref="InProcessTestClusterBuilder"/>中添加一个确定性提醒时钟。
    /// </summary>
    /// <param name="builder">测试组群构建器。</param>
    /// <param name="minimumReminderPeriod">可选择的最低提醒时间过重 。</param>
    /// <param name="refreshReminderListPeriod">提醒列表的复新期替换。</param>
    /// <param name="initializationTimeout">提醒启动超时覆盖选项。</param>
    /// <returns>所附的提醒测试钟。</returns>
    public static ReminderTestClock AddReminderTestClock(
        this InProcessTestClusterBuilder builder,
        TimeSpan? minimumReminderPeriod = null,
        TimeSpan? refreshReminderListPeriod = null,
        TimeSpan? initializationTimeout = null)
    {
        return ReminderTestClock.Attach(builder, minimumReminderPeriod, refreshReminderListPeriod, initializationTimeout);
    }
}
