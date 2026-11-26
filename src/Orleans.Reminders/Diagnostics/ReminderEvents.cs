using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Reminders.Diagnostics;

/// <summary>
/// 提供 Orleans 提醒事件的诊断听众和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class ReminderEvents
{
    /// <summary>
    /// 提醒事件诊断性听众的名字。
    /// </summary>
    public const string ListenerName = "Orleans.Reminders";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有提醒事件的可观测序列。
    /// </summary>
    public static IObservable<ReminderEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 获得一个可观测的提醒服务活动序列。
    /// </summary>
    public static IObservable<ReminderServiceEvent> ServiceEvents { get; } = new ServiceObservable();

    /// <summary>
    /// 用于提醒服务诊断事件的基准等级。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 如果与事件有关,请予以说明。</param>
    public abstract class ReminderServiceEvent(SiloAddress? siloAddress)
    {
        /// <summary>
        /// 与事件相关的 silo 地址(如果有的话)。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 当提醒服务完成启动并准备进行提醒操作时,事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 开始提供催复服务。</param>
    public sealed class ReminderServiceStarted(SiloAddress? siloAddress) : ReminderServiceEvent(siloAddress)
    {
    }

    /// <summary>
    /// 用于提醒诊断事件的基准类。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo 如果与事件有关,请予以说明。</param>
    public abstract class ReminderEvent(GrainId grainId, string reminderName, SiloAddress? siloAddress)
    {
        /// <summary>
        /// 下 grain 与教诲有关。
        /// </summary>
        public readonly GrainId GrainId = grainId;

        /// <summary>
        /// 提醒的名字。
        /// </summary>
        public readonly string ReminderName = reminderName;

        /// <summary>
        /// 与事件相关的 silo 地址(如果有的话)。
        /// </summary>
        public readonly SiloAddress? SiloAddress = siloAddress;
    }

    /// <summary>
    /// 登记或更新提醒时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class Registered(
        GrainId grainId,
        string reminderName,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
    }

    /// <summary>
    /// 提醒未登记时的事件有效载荷。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="siloAddress">大会主席的地址 silo 正在处理这个提醒</param>
    public sealed class Unregistered(
        GrainId grainId,
        string reminderName,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
    }

    /// <summary>
    /// 当地提醒计时器停止的原因
    /// </summary>
    public enum LocalReminderStopReason
    {
        /// <summary>
        /// 停止的原因不明。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 提醒函没有登记。
        /// </summary>
        Unregistered = 1,

        /// <summary>
        /// 未使用,因兼容性而保留
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        Replaced = 2,

        /// <summary>
        /// 提醒通知已移出 silo责任范围以外。
        /// </summary>
        RemovedFromRange = 3,

        /// <summary>
        /// 提醒函已从提醒表中删除。
        /// </summary>
        RemovedFromTable = 4,

        /// <summary>
        /// 提醒服务停止了。
        /// </summary>
        ServiceStopped = 5,

        /// <summary>
        /// 提醒不在服务处当前装载窗口内。
        /// </summary>
        OutsideLoadingWindow = 6,
    }

    /// <summary>
    /// 当 silo 启动提醒实例的本地计时器时, 事件有效载荷。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="identity">用于将整个生命周期事件之间本局部提醒实例联系起来的对象引用。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class LocalReminderStarted(
        GrainId grainId,
        string reminderName,
        object identity,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于将整个生命周期事件之间的局部提醒实例联系起来的对象引用。
        /// </summary>
        public readonly object Identity = identity;
    }

    /// <summary>
    /// 当 silo 停止提醒实例的本地计时器时, 事件有效载荷。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="identity">用于将整个生命周期事件之间本局部提醒实例联系起来的对象引用。</param>
    /// <param name="reason">本地定时器停止的原因。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class LocalReminderStopped(
        GrainId grainId,
        string reminderName,
        object identity,
        LocalReminderStopReason reason,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于将整个生命周期事件之间的局部提醒实例联系起来的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 当地定时器停止的原因
        /// </summary>
        public readonly LocalReminderStopReason Reason = reason;
    }

    /// <summary>
    /// 当本地提醒时间表失效时, 事件有效载荷被失效, 必须重新武装 。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="identity">用于将整个生命周期事件之间本局部提醒实例联系起来的对象引用。</param>
    /// <param name="scheduleVersion">与更改相关的本地调度版本。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class LocalReminderScheduleChanged(
        GrainId grainId,
        string reminderName,
        object identity,
        long scheduleVersion,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于将整个生命周期事件之间的局部提醒实例联系起来的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 与更改相关的本地调度版本 。
        /// </summary>
        public readonly long ScheduleVersion = scheduleVersion;
    }

    /// <summary>
    /// 当本地提醒器已武装等待下一个选中时, 事件有效载荷 。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="identity">用于将整个生命周期事件之间本局部提醒实例联系起来的对象引用。</param>
    /// <param name="scheduleVersion">与武装等待相关的本地调度版本。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class LocalReminderTickWaitArmed(
        GrainId grainId,
        string reminderName,
        object identity,
        long scheduleVersion,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 用于将整个生命周期事件之间的局部提醒实例联系起来的对象引用。
        /// </summary>
        public readonly object Identity = identity;

        /// <summary>
        /// 与武装等待有关的本地时间表版本。
        /// </summary>
        public readonly long ScheduleVersion = scheduleVersion;
    }

    /// <summary>
    /// 当提醒提示项即将着火时的事件有效载荷 。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="status">滴答状态传递到 grain。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class TickFiring(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 选中状态传递到 grain。
        /// </summary>
        public readonly TickStatus Status = status;
    }

    /// <summary>
    /// 提醒选中完成后的事件有效载荷。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="status">滴答状态传递到 grain。</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class TickCompleted(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 选中状态传递到 grain。
        /// </summary>
        public readonly TickStatus Status = status;
    }

    /// <summary>
    /// 提醒勾选失败时的事件有效载荷 。
    /// </summary>
    /// <param name="grainId">下 grain 与教诲有关。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="status">滴答状态传递到 grain。</param>
    /// <param name="exception">导致失败的例外</param>
    /// <param name="siloAddress">处理此提醒的 silo 地址。</param>
    public sealed class TickFailed(
        GrainId grainId,
        string reminderName,
        TickStatus status,
        Exception exception,
        SiloAddress? siloAddress) : ReminderEvent(grainId, reminderName, siloAddress)
    {
        /// <summary>
        /// 选中状态传递到 grain。
        /// </summary>
        public readonly TickStatus Status = status;

        /// <summary>
        /// 导致失败的例外
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
