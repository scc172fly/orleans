using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Orleans.Concurrency;

namespace Orleans.Runtime;

/// <summary>
/// 创建 grain 计时器的选项。
/// </summary>
public readonly struct GrainTimerCreationOptions() : IEquatable<GrainTimerCreationOptions>
{
    /// <summary>
    /// 初始化一个新的 <see cref="GrainTimerCreationOptions"/> 实例。
    /// </summary>
    /// <param name="dueTime">
    /// <see cref="TimeSpan"/> 表示在使用 <see cref="IGrainTimer"/> 构建时指定的回调方法之前需要延迟的时间。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 以阻止计时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 立即开始计时器。
    /// </param>
    /// <param name="period">
    /// 构建 <see cref="IGrainTimer"/> 时指定的回调方法的引用间隔时间。
    /// 指定用于禁用定期信号的 <see cref="Timeout.InfiniteTimeSpan "/> 。
    /// </param>
    [SetsRequiredMembers]
    public GrainTimerCreationOptions(TimeSpan dueTime, TimeSpan period) : this()
    {
        DueTime = dueTime;
        Period = period;
    }

    /// <summary>
    /// <see cref="TimeSpan"/> 表示在使用 <see cref="IGrainTimer"/> 构建时指定的回调方法之前需要延迟的时间。
    /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 以阻止计时器启动。
    /// 指定 <see cref="TimeSpan.Zero"/> 立即开始计时器。
    /// </summary>
    public required TimeSpan DueTime { get; init; }

    /// <summary>
    /// 构建 <see cref="IGrainTimer"/> 时指定的回调方法的引用间隔时间。
    /// 指定用于禁用定期信号的 <see cref="Timeout.InfiniteTimeSpan "/> 。
    /// </summary>
    public required TimeSpan Period { get; init; }

    /// <summary>
    /// 获得一个值,以显示是否允许该计时器所排定的回调与其他计时器和 grain 通话间断执行。
    /// 默认为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果此值为 <see langword="false"/>,则定时器回调将被处理类似 grain 呼叫的调用。如果 grain 调度表是重新输入的,则该计时器将被重新输入
    /// (i.e,它拥有 <see cref="ReentrantAttribute"/> 将允许回调时速
    /// 与其他 grain 电话和计时器互连,而不论该属性的价值如何。
    /// 如果该值是 <see langword="true"/>,则将允许计时器回调与其他定时器和 grain 调用中断。
    /// </remarks>
    public bool Interleave { get; init; }

    /// <summary>
    /// 获得一个值,以显示该计时器所计划回调是否延长 grain 激活的寿命。
    /// 默认为 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 如果此值为 <see langword="false"/>时针回调将不延 grain 激活的寿命。
    /// 如果 grain 仅处理此计时器的回调,而没有其他消息,则 grain 将在其闲置收集期结束后被收归。
    /// 如果该值是 <see langword="true"/>,则时针回调将延长一个 grain 激活的寿命。
    /// 如果计时周期短于 grain闲置收集期,则由于空闲原因将不收取 grain 。
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
    /// 确定两个创建时间器选项是否相等。
    /// </summary>
    public static bool operator ==(GrainTimerCreationOptions left, GrainTimerCreationOptions right) => left.Equals(right);

    /// <summary>
    /// 确定两个创建定时器选项是否不平等。
    /// </summary>
    public static bool operator !=(GrainTimerCreationOptions left, GrainTimerCreationOptions right) => !left.Equals(right);
}
