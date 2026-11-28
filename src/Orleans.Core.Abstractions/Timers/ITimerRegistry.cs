using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Timers;

/// <summary>
/// 提供管理 Grain 定时器的功能。
/// </summary>
public interface ITimerRegistry
{
    /// <summary>
    /// 创建 Grain 定时器。
    /// </summary>
    /// <param name="grainContext">与定时器关联的 Grain。</param>
    /// <param name="callback">定时器回调，每当定时器到期时触发。</param>
    /// <param name="state">传递给回调的状态对象。</param>
    /// <param name="dueTime">
    /// 调用 <paramref name="callback"/> 前需要等待的时长。
    /// 指定 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 可尽快调用回调。
    /// </param>
    /// <param name="period">
    /// <paramref name="callback"/> 的两次调用之间的时间间隔。
    /// 指定 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 可禁用周期性触发。
    /// </param>
    /// <returns>
    /// 表示定时器的 <see cref="IDisposable"/> 实例。
    /// </returns>
    [Obsolete("Use 'RegisterGrainTimer(grainContext, callback, state, new() { DueTime = dueTime, Period = period, Interleave = true })' instead.")]
    IDisposable RegisterTimer(IGrainContext grainContext, Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period);

    /// <inheritdoc cref="GrainBaseExtensions.RegisterGrainTimer{TState}(IGrainBase, Func{TState, CancellationToken, Task}, TState, GrainTimerCreationOptions)"/>
    /// <param name="grainContext">与定时器关联的 Grain。</param>
    /// <param name="callback">定时器回调，每当定时器到期时调用。</param>
    /// <param name="state">传递给回调的状态。</param>
    /// <param name="options">用于创建定时器的选项。</param>
    /// <typeparam name="TState"><paramref name="state"/> 参数的类型。</typeparam>
    IGrainTimer RegisterGrainTimer<TState>(IGrainContext grainContext, Func<TState, CancellationToken, Task> callback, TState state, GrainTimerCreationOptions options);
}
