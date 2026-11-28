using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Orleans.Concurrency;

namespace Orleans.Runtime;

/// <summary>
/// 用于创建 Grain 定时器的选项。
/// </summary>
public readonly struct GrainTimerCreationOptions() : IEquatable<GrainTimerCreationOptions>
{
    /// <summary>
    /// 初始化新的 <see cref="GrainTimerCreationOptions"/> 实例。
    /// </summary>
    /// <param name="dueTime">
    /// 一个 <see cref="TimeSpan"/>，表示调用创建 <see cref="IGrainTimer"/> 时指定的回调方法之前需要等待的时长。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 可立即启动定时器。
    /// </param>
    /// <param name="period">
    /// 创建 <see cref="IGrainTimer"/> 时指定的回调方法的调用时间间隔。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan "/> 可禁用周期性触发。
    /// </param>
    [SetsRequiredMembers]
    public GrainTimerCreationOptions(TimeSpan dueTime, TimeSpan period) : this()
    {
        DueTime = dueTime;
        Period = period;
    }

    /// <summary>
    /// 一个 <see cref="TimeSpan"/>，表示调用创建 <see cref="IGrainTimer"/> 时指定的回调方法之前需要等待的时长。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 可立即启动定时器。
    /// </summary>
    public required TimeSpan DueTime { get; init; }

    /// <summary>
    /// 创建 <see cref="IGrainTimer"/> 时指定的回调方法的调用时间间隔。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan "/> 可禁用周期性触发。
    /// </summary>
    public required TimeSpan Period { get; init; }

    /// <summary>
    /// 获取一个值，指示此定时器调度的回调是否允许与其他定时器及 Grain 调用交错执行。
    /// 默认值为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果此值为 <see langword="false"/>，定时器回调将被视同 Grain 调用。如果调度此定时器的 Grain 可重入
    /// （即其实现类应用了 <see cref="ReentrantAttribute"/> 特性），则定时器回调可以
    /// 与其他 Grain 调用及定时器交错执行，而不受此属性值的限制。
    /// 如果此值为 <see langword="true"/>，定时器回调可以与其他定时器及 Grain 调用交错执行。
    /// </remarks>
    public bool Interleave { get; init; }

    /// <summary>
    /// 获取一个值，指示此定时器调度的回调是否应延长 Grain 激活实例的生存期。
    /// 默认值为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果此值为 <see langword="false"/>，定时器回调不会延长 Grain 激活实例的生存期。
    /// 如果 Grain 仅处理此定时器的回调而未处理其他消息，则在空闲回收时间到期后会被回收。
    /// 如果此值为 <see langword="true"/>，定时器回调将延长 Grain 激活实例的生存期。
    /// 如果定时器周期短于 Grain 的空闲回收时间，则 Grain 不会因空闲而被回收。
    /// </remarks>
    public bool KeepAlive { get; init; }

    /// <inheritdoc/>
    public bool Equals(GrainTimerCreationOptions other) =>
        DueTime == other.DueTime
        && Period == other.Period
        && Interleave == other.Interleave
        && KeepAlive == other.KeepAlive;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GrainTimerCreationOptions other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(DueTime, Period, Interleave, KeepAlive);

    /// <summary>
    /// 判断两个定时器创建选项是否相等。
    /// </summary>
    public static bool operator ==(GrainTimerCreationOptions left, GrainTimerCreationOptions right) => left.Equals(right);

    /// <summary>
    /// 判断两个定时器创建选项是否不相等。
    /// </summary>
    public static bool operator !=(GrainTimerCreationOptions left, GrainTimerCreationOptions right) => !left.Equals(right);
}
