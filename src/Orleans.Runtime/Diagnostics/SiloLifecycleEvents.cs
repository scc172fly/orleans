using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans silo 生命周期事件的诊断听者及事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class SiloLifecycleEvents
{
    /// <summary>
    /// 生命周期事件 silo 诊断倾听器的名称。
    /// </summary>
    public const string ListenerName = "Orleans.SiloLifecycle";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有生命周期事件的可观测序列。
    /// </summary>
    public static IObservable<LifecycleEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于生命周期诊断事件的基本等级。
    /// </summary>
    public abstract class LifecycleEvent(
        int stage,
        string stageName,
        SiloAddress? siloAddress)
    {
        /// <summary>
        /// 数字阶段识别符 。
        /// </summary>
        public readonly int Stage = stage;

        /// <summary>
        /// 人类可读的舞台名称。
        /// </summary>
        public readonly string StageName = stageName;

        /// <summary>
        /// 下 silo 与事件相关的地址(如果有的话)。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 当生命周期阶段即将开始时, 事件有效载荷。
    /// </summary>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageStarting(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 当生命周期阶段成功完成时的事件有效载荷。
    /// </summary>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="elapsed">完成阶段所需的时间。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageCompleted(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 完成阶段所需的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 当生命周期阶段失败时的事件有效载荷。
    /// </summary>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="exception">导致故障的例外。</param>
    /// <param name="elapsed">失败前的时段。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageFailed(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        Exception exception,
        TimeSpan elapsed,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 导致失败的例外
        /// </summary>
        public readonly Exception Exception = exception;

        /// <summary>
        /// 失败之前的时光。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 当一个生命周期观察者即将开始运行时, 事件有效载荷的有效载荷 。
    /// </summary>
    /// <param name="observerName">观察员名。</param>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverStarting(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察员名。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 当一个生命周期观察员成功完成时,该事件的有效载荷。
    /// </summary>
    /// <param name="observerName">观察员名。</param>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="elapsed">完成工作所需时间。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverCompleted(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察员名。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 完成工作所需时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 当一个生命周期观察者失败时的事件有效载荷。
    /// </summary>
    /// <param name="observerName">观察员名。</param>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="exception">导致故障的例外。</param>
    /// <param name="elapsed">失败前的时段。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverFailed(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        Exception exception,
        TimeSpan elapsed,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察员名。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 导致失败的例外
        /// </summary>
        public readonly Exception Exception = exception;

        /// <summary>
        /// 失败之前的时光。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 当生命周期阶段即将停止时, 事件有效载荷。
    /// </summary>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageStopping(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 当生命周期阶段停止时,事件有效载荷。
    /// </summary>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="elapsed">需要时间来阻止舞台</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageStopped(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 需要时间来阻止舞台
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 当一个生命周期观察者即将停止时, 事件的有效载荷。
    /// </summary>
    /// <param name="observerName">观察员名。</param>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverStopping(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察员名。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 当一个生命周期观察者停止活动时,该事件的有效载荷。
    /// </summary>
    /// <param name="observerName">观察员名。</param>
    /// <param name="stage">数字级标识符。</param>
    /// <param name="stageName">人可读的舞台名称。</param>
    /// <param name="siloAddress">下 silo 与事件相关的地址(如果有的话)。</param>
    /// <param name="elapsed">需要时间来停止。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverStopped(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察员名。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 需要时间来停止。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    internal static void EmitObserverStarting(string observerName, int stage, string stageName, SiloAddress? siloAddress, ILifecycleObserver observer)
    {
        if (!Listener.IsEnabled(nameof(ObserverStarting)))
        {
            return;
        }

        Emit(observerName, stage, stageName, siloAddress, observer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string observerName, int stage, string stageName, SiloAddress? siloAddress, ILifecycleObserver observer)
        {
            Listener.Write(nameof(ObserverStarting), new ObserverStarting(
                observerName,
                stage,
                stageName,
                siloAddress,
                observer));
        }
    }

    internal static void EmitObserverCompleted(string observerName, int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObserver observer)
    {
        if (!Listener.IsEnabled(nameof(ObserverCompleted)))
        {
            return;
        }

        Emit(observerName, stage, stageName, siloAddress, elapsed, observer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string observerName, int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObserver observer)
        {
            Listener.Write(nameof(ObserverCompleted), new ObserverCompleted(
                observerName,
                stage,
                stageName,
                siloAddress,
                elapsed,
                observer));
        }
    }

    internal static void EmitObserverFailed(string observerName, int stage, string stageName, SiloAddress? siloAddress, Exception exception, TimeSpan elapsed, ILifecycleObserver observer)
    {
        if (!Listener.IsEnabled(nameof(ObserverFailed)))
        {
            return;
        }

        Emit(observerName, stage, stageName, siloAddress, exception, elapsed, observer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string observerName, int stage, string stageName, SiloAddress? siloAddress, Exception exception, TimeSpan elapsed, ILifecycleObserver observer)
        {
            Listener.Write(nameof(ObserverFailed), new ObserverFailed(
                observerName,
                stage,
                stageName,
                siloAddress,
                exception,
                elapsed,
                observer));
        }
    }

    internal static void EmitObserverStopping(string observerName, int stage, string stageName, SiloAddress? siloAddress, ILifecycleObserver observer)
    {
        if (!Listener.IsEnabled(nameof(ObserverStopping)))
        {
            return;
        }

        Emit(observerName, stage, stageName, siloAddress, observer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string observerName, int stage, string stageName, SiloAddress? siloAddress, ILifecycleObserver observer)
        {
            Listener.Write(nameof(ObserverStopping), new ObserverStopping(
                observerName,
                stage,
                stageName,
                siloAddress,
                observer));
        }
    }

    internal static void EmitObserverStopped(string observerName, int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObserver observer)
    {
        if (!Listener.IsEnabled(nameof(ObserverStopped)))
        {
            return;
        }

        Emit(observerName, stage, stageName, siloAddress, elapsed, observer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(string observerName, int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObserver observer)
        {
            Listener.Write(nameof(ObserverStopped), new ObserverStopped(
                observerName,
                stage,
                stageName,
                siloAddress,
                elapsed,
                observer));
        }
    }

    internal static void EmitStageCompleted(int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObservable lifecycle)
    {
        if (!Listener.IsEnabled(nameof(StageCompleted)))
        {
            return;
        }

        Emit(stage, stageName, siloAddress, elapsed, lifecycle);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObservable lifecycle)
        {
            Listener.Write(nameof(StageCompleted), new StageCompleted(
                stage,
                stageName,
                siloAddress,
                elapsed,
                lifecycle));
        }
    }

    internal static void EmitStageStopped(int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObservable lifecycle)
    {
        if (!Listener.IsEnabled(nameof(StageStopped)))
        {
            return;
        }

        Emit(stage, stageName, siloAddress, elapsed, lifecycle);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(int stage, string stageName, SiloAddress? siloAddress, TimeSpan elapsed, ILifecycleObservable lifecycle)
        {
            Listener.Write(nameof(StageStopped), new StageStopped(
                stage,
                stageName,
                siloAddress,
                elapsed,
                lifecycle));
        }
    }

    private sealed class Observable : IObservable<LifecycleEvent>
    {
        public IDisposable Subscribe(IObserver<LifecycleEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<LifecycleEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is LifecycleEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
