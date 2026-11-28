using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans Grain 激活事件的诊断监听器及事件数据类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但可能在次版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class GrainLifecycleEvents
{
    /// <summary>
    /// Grain 激活事件的诊断监听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.GrainLifecycle";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有 Grain 生命周期事件的可观察序列。
    /// </summary>
    public static IObservable<LifecycleEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 生命周期诊断事件使用的基类。
    /// </summary>
    public abstract class LifecycleEvent(IGrainContext grainContext)
    {
        /// <summary>
        /// 与事件关联的 Grain 上下文。
        /// </summary>
        public readonly IGrainContext GrainContext = grainContext;
    }

    /// <summary>
    /// Grain 激活实例创建时的事件数据。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    public sealed class Created(IGrainContext grainContext) : LifecycleEvent(grainContext)
    {
    }

    /// <summary>
    /// Grain 激活实例完成激活时的事件数据。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    public sealed class Activated(IGrainContext grainContext) : LifecycleEvent(grainContext)
    {
    }

    /// <summary>
    /// Grain 激活实例即将停用时的事件数据。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    /// <param name="reason">停用原因。</param>
    public sealed class Deactivating(
        IGrainContext grainContext,
        DeactivationReason reason) : LifecycleEvent(grainContext)
    {
        /// <summary>
        /// 停用原因。
        /// </summary>
        public readonly DeactivationReason Reason = reason;
    }

    /// <summary>
    /// Grain 激活实例完成停用时的事件数据。
    /// </summary>
    /// <param name="grainContext">Grain 上下文。</param>
    /// <param name="reason">停用原因。</param>
    public sealed class Deactivated(
        IGrainContext grainContext,
        DeactivationReason reason) : LifecycleEvent(grainContext)
    {
        /// <summary>
        /// 停用原因。
        /// </summary>
        public readonly DeactivationReason Reason = reason;
    }

    internal static void EmitActivated(IGrainContext grainContext)
    {
        if (!Listener.IsEnabled(nameof(Activated)))
        {
            return;
        }

        Emit(grainContext);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext)
        {
            Listener.Write(nameof(Activated), new Activated(grainContext));
        }
    }

    internal static void EmitCreated(IGrainContext grainContext)
    {
        if (!Listener.IsEnabled(nameof(Created)))
        {
            return;
        }

        Emit(grainContext);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext)
        {
            Listener.Write(nameof(Created), new Created(grainContext));
        }
    }

    internal static void EmitDeactivated(IGrainContext grainContext, DeactivationReason deactivationReason)
    {
        if (!Listener.IsEnabled(nameof(Deactivated)))
        {
            return;
        }

        Emit(grainContext, deactivationReason);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, DeactivationReason deactivationReason)
        {
            Listener.Write(nameof(Deactivated), new Deactivated(
                grainContext,
                deactivationReason));
        }
    }

    internal static void EmitDeactivating(IGrainContext grainContext, DeactivationReason deactivationReason)
    {
        if (!Listener.IsEnabled(nameof(Deactivating)))
        {
            return;
        }

        Emit(grainContext, deactivationReason);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, DeactivationReason deactivationReason)
        {
            Listener.Write(nameof(Deactivating), new Deactivating(
                grainContext,
                deactivationReason));
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
