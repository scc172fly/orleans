using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 为启动 Orleans grain 事件提供诊断听筒和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class GrainLifecycleEvents
{
    /// <summary>
    /// 启动 grain 事件的诊断倾听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.GrainLifecycle";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有 grain 生命周期事件的可观测序列。
    /// </summary>
    public static IObservable<LifecycleEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于生命周期诊断事件的基本等级。
    /// </summary>
    public abstract class LifecycleEvent(IGrainContext grainContext)
    {
        /// <summary>
        /// 与事件相关的 grain 上下文。
        /// </summary>
        public readonly IGrainContext GrainContext = grainContext;
    }

    /// <summary>
    /// 创建 grain 激活时的事件有效载荷。
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    public sealed class Created(IGrainContext grainContext) : LifecycleEvent(grainContext)
    {
    }

    /// <summary>
    /// 启动 grain 后事件有效载荷完成激活时有效载荷。
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    public sealed class Activated(IGrainContext grainContext) : LifecycleEvent(grainContext)
    {
    }

    /// <summary>
    /// 当 grain 激活即将停止时的事件有效载荷。
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    /// <param name="reason">停止使用的理由。</param>
    public sealed class Deactivating(
        IGrainContext grainContext,
        DeactivationReason reason) : LifecycleEvent(grainContext)
    {
        /// <summary>
        /// 停止使用的理由。
        /// </summary>
        public readonly DeactivationReason Reason = reason;
    }

    /// <summary>
    /// 当 grain 激活完成解除作用时,事件有效载荷。
    /// </summary>
    /// <param name="grainContext"> grain 上下文。</param>
    /// <param name="reason">停止使用的理由。</param>
    public sealed class Deactivated(
        IGrainContext grainContext,
        DeactivationReason reason) : LifecycleEvent(grainContext)
    {
        /// <summary>
        /// 停止使用的理由。
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
