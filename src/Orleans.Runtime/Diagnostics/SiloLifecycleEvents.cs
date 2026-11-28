using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans Silo 生命周期事件的诊断监听器及事件数据类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但可能在次版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class SiloLifecycleEvents
{
    /// <summary>
    /// Silo 生命周期事件的诊断监听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.SiloLifecycle";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有生命周期事件的可观察序列。
    /// </summary>
    public static IObservable<LifecycleEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 生命周期诊断事件使用的基类。
    /// </summary>
    public abstract class LifecycleEvent(
        int stage,
        string stageName,
        SiloAddress? siloAddress)
    {
        /// <summary>
        /// 数值形式的阶段标识。
        /// </summary>
        public readonly int Stage = stage;

        /// <summary>
        /// 便于阅读的阶段名称。
        /// </summary>
        public readonly string StageName = stageName;

        /// <summary>
        /// 与事件关联的 Silo 地址（如果有）。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 生命周期阶段即将启动时的事件数据。
    /// </summary>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
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
    /// 生命周期阶段成功完成时的事件数据。
    /// </summary>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="elapsed">完成该阶段所用的时间。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageCompleted(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 完成该阶段所用的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 生命周期阶段失败时的事件数据。
    /// </summary>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="exception">导致失败的异常。</param>
    /// <param name="elapsed">发生失败前经过的时间。</param>
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
        /// 导致失败的异常。
        /// </summary>
        public readonly Exception Exception = exception;

        /// <summary>
        /// 发生失败前经过的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 生命周期观察者即将启动时的事件数据。
    /// </summary>
    /// <param name="observerName">观察者名称。</param>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverStarting(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察者名称。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 生命周期观察者成功完成时的事件数据。
    /// </summary>
    /// <param name="observerName">观察者名称。</param>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="elapsed">完成所用的时间。</param>
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
        /// 观察者名称。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 完成所用的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 生命周期观察者失败时的事件数据。
    /// </summary>
    /// <param name="observerName">观察者名称。</param>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="exception">导致失败的异常。</param>
    /// <param name="elapsed">发生失败前经过的时间。</param>
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
        /// 观察者名称。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 导致失败的异常。
        /// </summary>
        public readonly Exception Exception = exception;

        /// <summary>
        /// 发生失败前经过的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 生命周期阶段即将停止时的事件数据。
    /// </summary>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
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
    /// 生命周期阶段已停止时的事件数据。
    /// </summary>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="elapsed">停止该阶段所用的时间。</param>
    /// <param name="lifecycle">生命周期实例。</param>
    public sealed class StageStopped(
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        TimeSpan elapsed,
        ILifecycleObservable lifecycle) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 停止该阶段所用的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 生命周期实例。
        /// </summary>
        public readonly ILifecycleObservable Lifecycle = lifecycle;
    }

    /// <summary>
    /// 生命周期观察者即将停止时的事件数据。
    /// </summary>
    /// <param name="observerName">观察者名称。</param>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="observer">生命周期观察者。</param>
    public sealed class ObserverStopping(
        string observerName,
        int stage,
        string stageName,
        SiloAddress? siloAddress,
        ILifecycleObserver observer) : LifecycleEvent(stage, stageName, siloAddress)
    {
        /// <summary>
        /// 观察者名称。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 生命周期观察者。
        /// </summary>
        public readonly ILifecycleObserver Observer = observer;
    }

    /// <summary>
    /// 生命周期观察者已停止时的事件数据。
    /// </summary>
    /// <param name="observerName">观察者名称。</param>
    /// <param name="stage">阶段的数字标识符。</param>
    /// <param name="stageName">便于阅读的阶段名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果有）。</param>
    /// <param name="elapsed">停止所用的时间。</param>
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
        /// 观察者名称。
        /// </summary>
        public readonly string ObserverName = observerName;

        /// <summary>
        /// 停止所用的时间。
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
