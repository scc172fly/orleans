namespace Orleans.Reminders;

/// <summary>
/// 用于解决提醒子系统从按键依赖性 <see cref="System.TimeProvider"/> 中调出提醒分系统的服务关键键
/// 。请参见核心运行时间区域 <see cref="Orleans.Runtime.TimeProviderNames"/> 。
/// </summary>
public static class ReminderTimeProviderNames
{
    /// <summary>
    /// 提醒子系统使用的时钟
    /// </summary>
    public const string Reminders = "Orleans.Reminders";
}
