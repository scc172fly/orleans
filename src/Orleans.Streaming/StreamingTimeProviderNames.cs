namespace Orleans.Streams;

/// <summary>
/// 用于从键控依赖注入中解析流处理子系统的 <see cref="System.TimeProvider"/> 的服务键。
/// 有关核心运行时区域，请参见 <see cref="Orleans.Runtime.TimeProviderNames"/>。
/// </summary>
public static class StreamingTimeProviderNames
{
    /// <summary>
    /// 流处理子系统使用的时钟（拉取代理及其管理器、队列均衡器）。
    /// </summary>
    public const string Streaming = "Orleans.Streaming";
}
