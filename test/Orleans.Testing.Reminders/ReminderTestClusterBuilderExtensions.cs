using Orleans.TestingHost;

namespace Orleans.Testing.Reminders;

/// <summary>
/// 用于在 <see cref="InProcessTestClusterBuilder"/> 上启用确定性提醒器测试行为的扩展方法。
/// </summary>
public static class ReminderTestClusterBuilderExtensions
{
    /// <summary>
    /// 向指定的 <see cref="InProcessTestClusterBuilder"/> 添加确定性提醒器时钟。
    /// </summary>
    /// <param name="builder">测试集群构建器。</param>
    /// <param name="minimumReminderPeriod">可选的最短提醒器周期覆盖值。</param>
    /// <param name="refreshReminderListPeriod">可选的提醒器列表刷新周期覆盖值。</param>
    /// <param name="initializationTimeout">可选的提醒器初始化超时覆盖值。</param>
    /// <returns>已附加的提醒器测试时钟。</returns>
    public static ReminderTestClock AddReminderTestClock(
        this InProcessTestClusterBuilder builder,
        TimeSpan? minimumReminderPeriod = null,
        TimeSpan? refreshReminderListPeriod = null,
        TimeSpan? initializationTimeout = null)
    {
        return ReminderTestClock.Attach(builder, minimumReminderPeriod, refreshReminderListPeriod, initializationTimeout);
    }
}
