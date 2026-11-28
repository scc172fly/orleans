using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Reminders.Diagnostics;

/// <summary>
/// 提供 Orleans 提醒器事件的诊断侦听器及事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但在次要版本之间可能发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class ReminderEvents
{
    /// <summary>
    /// 提醒器事件的诊断侦听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.Reminders";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有提醒器事件的可观察序列。
    /// </summary>
    public static IObservable<ReminderEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 获取提醒器服务事件的可观察序列。
    /// </summary>
    public static IObservable<ReminderServiceEvent> ServiceEvents { get; } = new ServiceObservable();

    /// <summary>
    /// 提醒器服务诊断事件使用的基类。
    /// </summary>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果存在）。</param>
    public abstract class ReminderServiceEvent(SiloAddress? siloAddress)
    {
        /// <summary>
        /// 与事件关联的 Silo 地址（如果存在）。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 提醒器服务完成启动并准备好执行提醒器操作时的事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">提醒器服务已启动的 Silo 的地址。</param>
    public sealed class ReminderServiceStarted(SiloAddress? siloAddress) : ReminderServiceEvent(siloAddress)
    {
    }

    /// <summary>
    /// 提醒器诊断事件使用的基类。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="siloAddress">与事件关联的 Silo 地址（如果存在）。</param>
    public abstract class ReminderEvent(GrainId grainId, string reminderName, SiloAddress? siloAddress)
    {
        /// <summary>
        /// 与提醒器关联的 Grain。
        /// </summary>
        public readonly GrainId GrainId = grainId;

        /// <summary>
        /// 提醒器名称。
        /// </summary>
        public readonly string ReminderName = reminderName;

        /// <summary>
        /// 与事件关联的 Silo 地址（如果存在）。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 注册或更新提醒器时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class Registered(
        GrainId grainId,
        string reminderName,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
    }

    /// <summary>
    /// 注销提醒器时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="siloAddress">之前负责处理该提醒器的 Silo 的地址。</param>
    public sealed class Unregistered(
        GrainId grainId,
        string reminderName,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
    }

    /// <summary>
    /// 本地提醒器定时器停止的原因。
    /// </summary>
    public enum LocalReminderStopReason
    {
        /// <summary>
        /// 停止原因未知。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 提醒器已注销。
        /// </summary>
        Unregistered = 1,

        /// <summary>
        /// 未使用。为兼容性而保留。
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        Replaced = 2,

        /// <summary>
        /// 提醒器已移出此 Silo 的负责范围。
        /// </summary>
        RemovedFromRange = 3,

        /// <summary>
        /// 提醒器已从提醒器表中移除。
        /// </summary>
        RemovedFromTable = 4,

        /// <summary>
        /// 提醒器服务已停止。
        /// </summary>
        ServiceStopped = 5,

        /// <summary>
        /// 提醒器位于服务当前的加载时间窗口之外。
        /// </summary>
        OutsideLoadingWindow = 6,
    }

    /// <summary>
    /// Silo 为某个提醒器实例启动本地定时器时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="identity">用于关联该本地提醒器实例各生命周期事件的对象引用。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class LocalReminderStarted(
        GrainId grainId,
        string reminderName,
        object identity,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于关联该本地提醒器实例各生命周期事件的对象引用。
        /// </summary>
        public readonly object Identity = identity;
    }

    /// <summary>
    /// Silo 停止某个提醒器实例的本地定时器时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="identity">用于关联该本地提醒器实例各生命周期事件的对象引用。</param>
    /// <param name="reason">本地定时器停止的原因。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class LocalReminderStopped(
        GrainId grainId,
        string reminderName,
        object identity,
        LocalReminderStopReason reason,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于关联该本地提醒器实例各生命周期事件的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 本地定时器停止的原因。
        /// </summary>
        public readonly LocalReminderStopReason Reason = reason;
    }

    /// <summary>
    /// 本地提醒器调度失效、需要重新安排等待时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="identity">用于关联该本地提醒器实例各生命周期事件的对象引用。</param>
    /// <param name="scheduleVersion">与此次变更关联的本地调度版本。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class LocalReminderScheduleChanged(
        GrainId grainId,
        string reminderName,
        object identity,
        long scheduleVersion,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于关联该本地提醒器实例各生命周期事件的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 与此次变更关联的本地调度版本。
        /// </summary>
        public readonly long ScheduleVersion = scheduleVersion;
    }

    /// <summary>
    /// 本地提醒器已开始等待下次触发时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="identity">用于关联该本地提醒器实例各生命周期事件的对象引用。</param>
    /// <param name="scheduleVersion">与已启动的等待关联的本地调度版本。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class LocalReminderTickWaitArmed(
        GrainId grainId,
        string reminderName,
        object identity,
        long scheduleVersion,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于关联该本地提醒器实例各生命周期事件的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 与已启动的等待关联的本地调度版本。
        /// </summary>
        public readonly long ScheduleVersion = scheduleVersion;
    }

    /// <summary>
    /// 提醒器即将触发时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="status">传递给 Grain 的触发状态。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class TickFiring(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 传递给 Grain 的触发状态。
        /// </summary>
        public readonly TickStatus Status = status;
    }

    /// <summary>
    /// 提醒器触发处理成功完成时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="status">传递给 Grain 的触发状态。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class TickCompleted(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 传递给 Grain 的触发状态。
        /// </summary>
        public readonly TickStatus Status = status;
    }

    /// <summary>
    /// 提醒器触发处理失败时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">与提醒器关联的 Grain。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="status">传递给 Grain 的触发状态。</param>
    /// <param name="exception">导致失败的异常。</param>
    /// <param name="siloAddress">负责处理该提醒器的 Silo 的地址。</param>
    public sealed class TickFailed(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        Exception exception,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 传递给 Grain 的触发状态。
        /// </summary>
        public readonly TickStatus Status = status;

        /// <summary>
        /// 导致失败的异常。
        /// </summary>
        public readonly Exception Exception = exception;
    }

    internal static void EmitRegistered(GrainId grainId, string reminderName, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(Registered)))
        {
            return;
        }

        Emit(grainId, reminderName, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(Registered), new Registered(
                grainId,
                reminderName,
                siloAddress));
        }
    }

    internal static void EmitReminderServiceStarted(SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(ReminderServiceStarted)))
        {
            return;
        }

        Emit(siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress? siloAddress)
        {
            Listener.Write(nameof(ReminderServiceStarted), new ReminderServiceStarted(siloAddress));
        }
    }

    internal static void EmitUnregistered(GrainId grainId, string reminderName, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(Unregistered)))
        {
            return;
        }

        Emit(grainId, reminderName, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(Unregistered), new Unregistered(
                grainId,
                reminderName,
                siloAddress));
        }
    }

    internal static void EmitLocalReminderStarted(GrainId grainId, string reminderName, object identity, SiloAddress? siloAddress)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!Listener.IsEnabled(nameof(LocalReminderStarted)))
        {
            return;
        }

        Emit(grainId, reminderName, identity, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, object identity, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(LocalReminderStarted), new LocalReminderStarted(
                grainId,
                reminderName,
                identity,
                siloAddress));
        }
    }

    internal static void EmitLocalReminderStopped(GrainId grainId, string reminderName, object identity, LocalReminderStopReason reason, SiloAddress? siloAddress)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!Listener.IsEnabled(nameof(LocalReminderStopped)))
        {
            return;
        }

        Emit(grainId, reminderName, identity, reason, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, object identity, LocalReminderStopReason reason, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(LocalReminderStopped), new LocalReminderStopped(
                grainId,
                reminderName,
                identity,
                reason,
                siloAddress));
        }
    }

    internal static void EmitLocalReminderScheduleChanged(GrainId grainId, string reminderName, object identity, long scheduleVersion, SiloAddress? siloAddress)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!Listener.IsEnabled(nameof(LocalReminderScheduleChanged)))
        {
            return;
        }

        Emit(grainId, reminderName, identity, scheduleVersion, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, object identity, long scheduleVersion, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(LocalReminderScheduleChanged), new LocalReminderScheduleChanged(
                grainId,
                reminderName,
                identity,
                scheduleVersion,
                siloAddress));
        }
    }

    internal static void EmitLocalReminderTickWaitArmed(GrainId grainId, string reminderName, object identity, long scheduleVersion, SiloAddress? siloAddress)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!Listener.IsEnabled(nameof(LocalReminderTickWaitArmed)))
        {
            return;
        }

        Emit(grainId, reminderName, identity, scheduleVersion, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, object identity, long scheduleVersion, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(LocalReminderTickWaitArmed), new LocalReminderTickWaitArmed(
                grainId,
                reminderName,
                identity,
                scheduleVersion,
                siloAddress));
        }
    }

    internal static void EmitTickFiring(GrainId grainId, string reminderName, TickStatus status, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(TickFiring)))
        {
            return;
        }

        Emit(grainId, reminderName, status, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, TickStatus status, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(TickFiring), new TickFiring(
                grainId,
                reminderName,
                status,
                siloAddress));
        }
    }

    internal static void EmitTickCompleted(GrainId grainId, string reminderName, TickStatus status, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(TickCompleted)))
        {
            return;
        }

        Emit(grainId, reminderName, status, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, TickStatus status, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(TickCompleted), new TickCompleted(
                grainId,
                reminderName,
                status,
                siloAddress));
        }
    }

    internal static void EmitTickFailed(GrainId grainId, string reminderName, TickStatus status, Exception exception, SiloAddress? siloAddress)
    {
        if (!Listener.IsEnabled(nameof(TickFailed)))
        {
            return;
        }

        Emit(grainId, reminderName, status, exception, siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(GrainId grainId, string reminderName, TickStatus status, Exception exception, SiloAddress? siloAddress)
        {
            Listener.Write(nameof(TickFailed), new TickFailed(
                grainId,
                reminderName,
                status,
                exception,
                siloAddress));
        }
    }

    private sealed class Observable : IObservable<ReminderEvent>
    {
        public IDisposable Subscribe(IObserver<ReminderEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<ReminderEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is ReminderEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }

    private sealed class ServiceObservable : IObservable<ReminderServiceEvent>
    {
        public IDisposable Subscribe(IObserver<ReminderServiceEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<ReminderServiceEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is ReminderServiceEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
