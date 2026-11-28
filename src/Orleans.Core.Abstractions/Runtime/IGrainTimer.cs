using System;
using System.Threading;

namespace Orleans.Runtime;

/// <summary>
/// 表示属于某个 Grain 的定时器。
/// </summary>
public interface IGrainTimer : IDisposable
{
    /// <summary>更改定时器的开始时间及方法调用间隔，使用 <see cref="TimeSpan"/> 值衡量时间间隔。</summary>
    /// <param name="dueTime">
    /// 一个 <see cref="TimeSpan"/>，表示调用创建 <see cref="IGrainTimer"/> 时指定的回调方法之前需要等待的时长。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器重新启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 可立即重新启动定时器。
    /// </param>
    /// <param name="period">
    /// 创建定时器时指定的回调方法的调用时间间隔。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可禁用周期性触发。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> 或 <paramref name="period"/> 参数的毫秒数小于 -1 或大于 4294967294。</exception>
    void Change(TimeSpan dueTime, TimeSpan period);
}
