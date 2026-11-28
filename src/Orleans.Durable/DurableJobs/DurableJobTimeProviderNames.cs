namespace Orleans.Durable.Jobs;

/// <summary>
/// 用于解析持久化作业子系统的 <see cref="System.TimeProvider"/> 的服务键，通过键控依赖
/// 注入进行解析。有关核心运行时各区域，请参阅 <see cref="Orleans.Runtime.TimeProviderNames"/>。
/// </summary>
public static class DurableJobTimeProviderNames
{
    /// <summary>
    /// 持久化作业子系统使用的时钟。
    /// </summary>
    public const string DurableJobs = "Orleans.DurableJobs";
}
