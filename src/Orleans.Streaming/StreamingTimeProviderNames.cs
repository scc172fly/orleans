namespace Orleans.Streams;

/// <summary>
/// 用于解决来自按键依赖的流子子 <see cref="System.TimeProvider"/> 的连接
/// 。请参见核心运行时间区域 <see cref="Orleans.Runtime.TimeProviderNames"/> 。
/// </summary>
public static class StreamingTimeProviderNames
{
    /// <summary>
    /// 流子系统(拉动剂和管理人员、队列平衡器)使用的时钟。
    /// </summary>
    public const string Streaming = "Orleans.Streaming";
}
