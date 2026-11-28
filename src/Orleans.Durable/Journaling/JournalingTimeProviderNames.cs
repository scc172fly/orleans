namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于从带键依赖注入解析日志持久化子系统的 <see cref="System.TimeProvider"/> 的服务键。
/// 有关核心运行时区域，参见 <see cref="Orleans.Runtime.TimeProviderNames"/>。
/// </summary>
public static class JournalingTimeProviderNames
{
    /// <summary>
    /// 日志持久化子系统使用的时钟。
    /// </summary>
    public const string Journaling = "Orleans.Journaling";
}
