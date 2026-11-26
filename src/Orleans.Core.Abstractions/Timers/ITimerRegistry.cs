using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Timers;

/// <summary>
/// 管理 grain 计时器的功能。
/// </summary>
public interface ITimerRegistry
{
    /// <summary>
    /// 创建 grain 计时器。
    /// </summary>
    /// <param name="grainContext">计时器关联的 grain 。</param>
    /// <param name="callback">计时器回调, 定时器到期后即会点火。</param>
    /// <param name="state">国家对象传递到回调。</param>
    /// <param name="dueTime">
    /// 援引 <paramref name="callback"/> 之前的延误时间。
    /// 指定 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 以阻止计时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 迅速调用回调。
    /// </param>
    /// <param name="period">
    /// <paramref name="callback"/>的引用之间的时间间隔。
    /// 指定用于禁用定期信号的 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 。
    /// </param>
    /// <returns>
    /// <see cref="IDisposable"/> 实例,它代表计时器。
    /// </returns>
    [Obsolete("Use 'RegisterGrainTimer(grainContext, callback, state, new() { DueTime = dueTime, Period = period, Interleave = true })' instead.")]
    IDisposable RegisterTimer(IGrainContext grainContext, Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period);

    /// <inheritdoc cref="GrainBaseExtensions.RegisterGrainTimer{TState}(IGrainBase, Func{TState, CancellationToken, Task}, TState, GrainTimerCreationOptions)"/>
    /// <param name="grainContext">计时器关联的 grain 。</param>
    /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
    /// <param name="state">国家通过回调。</param>
    /// <param name="options">创建计时器的选项。</param>
    /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
    IGrainTimer RegisterGrainTimer<TState>(IGrainContext grainContext, Func<TState, CancellationToken, Task> callback, TState state, GrainTimerCreationOptions options);
}
