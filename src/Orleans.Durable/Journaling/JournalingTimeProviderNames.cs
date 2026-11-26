namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于解决按键依赖性中子子日记 <see cref="System.TimeProvider"/> 的服务密钥
/// 。请参见核心运行时间区域 <see cref="Orleans.Runtime.TimeProviderNames"/> 。
/// </summary>
public static class JournalingTimeProviderNames
{
    /// <summary>
    /// 日记子系统使用的时钟
    /// </summary>
    public const string Journaling = "Orleans.Journaling";
}
