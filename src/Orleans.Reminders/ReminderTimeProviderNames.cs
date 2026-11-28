namespace Orleans.Reminders;

/// <summary>
/// 用于通过键控依赖注入解析提醒器子系统 <see cref="System.TimeProvider"/> 的服务键。
/// 核心运行时各区域使用的服务键参见 <see cref="Orleans.Runtime.TimeProviderNames"/>。
/// </summary>
public static class ReminderTimeProviderNames
{
    /// <summary>
    /// 提醒器子系统使用的时钟。
    /// </summary>
    public const string Reminders = "Orleans.Reminders";
}
