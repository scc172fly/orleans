using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供诊断听者类型和 Orleans grain 定时器事件的有效载荷类型的诊断听者。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class GrainTimerEvents
{
    /// <summary>
    /// 用于 grain 定时器事件的诊断倾听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.GrainTimers";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有定时事件可观察到的序列。
    /// </summary>
    public static IObservable<TimerEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于定时器诊断事件的基准等级 。
    /// </summary>
    public abstract class TimerEvent(IGrainContext grainContext, IGrainTimer timer)
    {
        /// <summary>
        /// 拥有计时器的 grain 上下文。
        /// </summary>
        public readonly IGrainContext GrainContext = grainContext;

        /// <summary>
        /// 与事件相关的计时器 。
        /// </summary>
        public readonly IGrainTimer Timer = timer;
    }

    /// <summary>
    /// 创建 grain 计时器时的事件有效载荷。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    /// <param name="dueTime">计时器的初始到期时间 。</param>
    /// <param name="period">计时周期。</param>
    public sealed class Created(
        IGrainContext grainContext,
        IGrainTimer timer,
        TimeSpan dueTime,
        TimeSpan period) : TimerEvent(grainContext, timer)
    {
        /// <summary>
        /// 计时器的初始到期时间 。
        /// </summary>
        public readonly TimeSpan DueTime = dueTime;

        /// <summary>
        /// 定时期
        /// </summary>
        public readonly TimeSpan Period = period;
    }

    /// <summary>
    /// 事件有效载荷, 当 grain 计时器回调即将启动时使用。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    public sealed class TickStart(
        IGrainContext grainContext,
        IGrainTimer timer) : TimerEvent(grainContext, timer)
    {
    }

    /// <summary>
    /// 事件有效载荷, 当 grain 计时器回调完成后使用。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    /// <param name="exception">被回调的例外,如果有的话</param>
    public sealed class TickStop(
        IGrainContext grainContext,
        IGrainTimer timer,
        Exception? exception) : TimerEvent(grainContext, timer)
    {
        /// <summary>
        /// 被回调的例外,如果有的话
        /// </summary>
        public readonly Exception? Exception = exception;
    }

    /// <summary>
    /// 处置 grain 计时器时的事件有效载荷。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    public sealed class Disposed(
        IGrainContext grainContext,
        IGrainTimer timer) : TimerEvent(grainContext, timer)
    {
    }

    internal static void EmitCreated(IGrainContext grainContext, TimeSpan dueTime, TimeSpan period, IGrainTimer timer)
    {
        if (!Listener.IsEnabled(nameof(Created)))
        {
            return;
        }

        Emit(grainContext, dueTime, period, timer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TimeSpan dueTime, TimeSpan period, IGrainTimer timer)
        {
            Listener.Write(nameof(Created), new Created(
                grainContext,
                timer,
                dueTime,
                period));
        }
    }

    internal static void EmitDisposed(IGrainContext grainContext, IGrainTimer timer)
    {
        if (!Listener.IsEnabled(nameof(Disposed)))
        {
            return;
        }

        Emit(grainContext, timer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, IGrainTimer timer)
        {
            Listener.Write(nameof(Disposed), new Disposed(
                grainContext,
                timer));
        }
    }

    internal static void EmitTickStart(IGrainContext grainContext, IGrainTimer timer)
    {
        if (!Listener.IsEnabled(nameof(TickStart)))
        {
            return;
        }

        Emit(grainContext, timer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, IGrainTimer timer)
        {
            Listener.Write(nameof(TickStart), new TickStart(
                grainContext,
                timer));
        }
    }

    internal static void EmitTickStop(IGrainContext grainContext, IGrainTimer timer, Exception? exception = null)
    {
        if (!Listener.IsEnabled(nameof(TickStop)))
        {
            return;
        }

        Emit(grainContext, timer, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, IGrainTimer timer, Exception? exception)
        {
            Listener.Write(nameof(TickStop), new TickStop(
                grainContext,
                timer,
                exception));
        }
    }

    private sealed class Observable : IObservable<TimerEvent>
    {
        public IDisposable Subscribe(IObserver<TimerEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<TimerEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is TimerEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
