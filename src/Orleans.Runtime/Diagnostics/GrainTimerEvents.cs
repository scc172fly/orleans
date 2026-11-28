using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans Grain 定时器事件的诊断监听器及事件数据类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但可能在次版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class GrainTimerEvents
{
    /// <summary>
    /// Grain 定时器事件的诊断监听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.GrainTimers";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有定时器事件的可观察序列。
    /// </summary>
    public static IObservable<TimerEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 定时器诊断事件使用的基类。
    /// </summary>
    public abstract class TimerEvent(IGrainContext grainContext, IGrainTimer timer)
    {
        /// <summary>
        /// 拥有定时器的 Grain 上下文。
        /// </summary>
        public readonly IGrainContext GrainContext = grainContext;

        /// <summary>
        /// 与事件关联的定时器。
        /// </summary>
        public readonly IGrainTimer Timer = timer;
    }

    /// <summary>
    /// Grain 定时器创建时的事件数据。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 Grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    /// <param name="dueTime">定时器的初始触发延迟。</param>
    /// <param name="period">定时器周期。</param>
    public sealed class Created(
        IGrainContext grainContext,
        IGrainTimer timer,
        TimeSpan dueTime,
        TimeSpan period) : TimerEvent(grainContext, timer)
    {
        /// <summary>
        /// 定时器的初始触发延迟。
        /// </summary>
        public readonly TimeSpan DueTime = dueTime;

        /// <summary>
        /// 定时器周期。
        /// </summary>
        public readonly TimeSpan Period = period;
    }

    /// <summary>
    /// Grain 定时器触发回调即将开始时的事件数据。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 Grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    public sealed class TickStart(
        IGrainContext grainContext,
        IGrainTimer timer) : TimerEvent(grainContext, timer)
    {
    }

    /// <summary>
    /// Grain 定时器触发回调完成时的事件数据。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 Grain 上下文。</param>
    /// <param name="timer">定时器实例。</param>
    /// <param name="exception">回调抛出的异常（如果有）。</param>
    public sealed class TickStop(
        IGrainContext grainContext,
        IGrainTimer timer,
        Exception? exception) : TimerEvent(grainContext, timer)
    {
        /// <summary>
        /// 回调抛出的异常（如果有）。
        /// </summary>
        public readonly Exception? Exception = exception;
    }

    /// <summary>
    /// Grain 定时器释放时的事件数据。
    /// </summary>
    /// <param name="grainContext">拥有定时器的 Grain 上下文。</param>
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
