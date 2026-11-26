using System;
using System.Threading;

namespace Orleans.Runtime;

/// <summary>
/// 表示属于 grain的计时器。
/// </summary>
public interface IGrainTimer : IDisposable
{
    /// <summary>使用 <see cref="TimeSpan"/> 值来测量时间间隔,修改计时器的计算方法之间的起始时间和间隔。</summary>
    /// <param name="dueTime">
    /// <see cref="TimeSpan"/> 表示在使用 <see cref="IGrainTimer"/> 构建时指定的回调方法之前需要延迟的时间。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 以防止计时器重新启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 立即重新启动计时器。
    /// </param>
    /// <param name="period">
    /// 安装计时器时指定的回调方法的调用间隔。
    /// 指定用于禁用定期信号的 <see cref="Timeout.InfiniteTimeSpan"/> 。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">下 <paramref name="dueTime"/> 或 <paramref name="period"/> 参数,以毫秒计,小于 -1或大于 4294967294。</exception>
    void Change(TimeSpan dueTime, TimeSpan period);
}
