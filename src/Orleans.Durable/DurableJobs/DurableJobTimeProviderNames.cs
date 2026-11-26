namespace Orleans.Durable.Jobs;

/// <summary>
/// 用于解决长效工作子系统(Q0Q)的按键依赖性 <see cref="System.TimeProvider"/> 的服务关键
/// 。请参见核心运行时间区域 <see cref="Orleans.Runtime.TimeProviderNames"/> 。
/// </summary>
public static class DurableJobTimeProviderNames
{
    /// <summary>
    /// 持久工作子系统使用的时钟
    /// </summary>
    public const string DurableJobs = "Orleans.DurableJobs";
}
