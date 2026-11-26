using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans 激活再平衡事件诊断听者及事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class ActivationRebalancerEvents
{
    /// <summary>
    /// 激活再平衡事件诊断倾听器的名称 。
    /// </summary>
    public const string ListenerName = "Orleans.ActivationRebalancer";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有激活的重新平衡事件的可观测序列。
    /// </summary>
    public static IObservable<RebalancerEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于激活再平衡诊断事件的基准等级。
    /// </summary>
    public abstract class RebalancerEvent(SiloAddress siloAddress)
    {
        /// <summary>
        /// 重新平衡主机所在的 silo 地址 。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;
    }

    /// <summary>
    /// 当重新平衡周期开始时, 事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 正在重新平衡。</param>
    /// <param name="cycleNumber">本届会议的周期编号。</param>
    public sealed class CycleStart(
        SiloAddress siloAddress,
        int cycleNumber) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 本届会议的周期编号。
        /// </summary>
        public readonly int CycleNumber = cycleNumber;
    }

    /// <summary>
    /// 事件有效载荷 当重新平衡周期完成。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 正在重新平衡。</param>
    /// <param name="cycleNumber">本届会议的周期编号。</param>
    /// <param name="activationsMigrated">周期期间迁移的激活数量。</param>
    /// <param name="entropyDeviation">周期后的星环偏差。</param>
    /// <param name="elapsed">完成周期所需时间。</param>
    /// <param name="sessionCompleted">这一周期是否导致届会结束。</param>
    public sealed class CycleStop(
        SiloAddress siloAddress,
        int cycleNumber,
        int activationsMigrated,
        double entropyDeviation,
        TimeSpan elapsed,
        bool sessionCompleted) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 本届会议的周期编号。
        /// </summary>
        public readonly int CycleNumber = cycleNumber;

        /// <summary>
        /// 周期内迁移的激活次数。
        /// </summary>
        public readonly int ActivationsMigrated = activationsMigrated;

        /// <summary>
        /// 循环后的环流变化
        /// </summary>
        public readonly double EntropyDeviation = entropyDeviation;

        /// <summary>
        /// 完成周期所需时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 表明这一周期是否导致届会结束。
        /// </summary>
        public readonly bool SessionCompleted = sessionCompleted;
    }

    /// <summary>
    /// 当重新平衡会话开始时的事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 正在重新平衡。</param>
    public sealed class SessionStart(SiloAddress siloAddress) : RebalancerEvent(siloAddress)
    {
    }

    /// <summary>
    /// 当再平衡会话停止时, 事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 正在重新平衡。</param>
    /// <param name="reason">届会停止的原因。</param>
    /// <param name="totalCycles">届会完成的周期总数。</param>
    public sealed class SessionStop(
        SiloAddress siloAddress,
        string reason,
        int totalCycles) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 之所以会停止了
        /// </summary>
        public readonly string Reason = reason;

        /// <summary>
        /// 届会完成的周期总数。
        /// </summary>
        public readonly int TotalCycles = totalCycles;
    }

    internal static void EmitCycleStart(SiloAddress siloAddress, int cycleNumber)
    {
        if (!Listener.IsEnabled(nameof(CycleStart)))
        {
            return;
        }

        Emit(siloAddress, cycleNumber);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress, int cycleNumber)
        {
            Listener.Write(nameof(CycleStart), new CycleStart(
                siloAddress,
                cycleNumber));
        }
    }

    internal static void EmitCycleStop(SiloAddress siloAddress, int cycleNumber, int activationsMigrated, double entropyDeviation, TimeSpan elapsed, bool sessionCompleted)
    {
        if (!Listener.IsEnabled(nameof(CycleStop)))
        {
            return;
        }

        Emit(siloAddress, cycleNumber, activationsMigrated, entropyDeviation, elapsed, sessionCompleted);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress, int cycleNumber, int activationsMigrated, double entropyDeviation, TimeSpan elapsed, bool sessionCompleted)
        {
            Listener.Write(nameof(CycleStop), new CycleStop(
                siloAddress,
                cycleNumber,
                activationsMigrated,
                entropyDeviation,
                elapsed,
                sessionCompleted));
        }
    }

    internal static void EmitSessionStart(SiloAddress siloAddress)
    {
        if (!Listener.IsEnabled(nameof(SessionStart)))
        {
            return;
        }

        Emit(siloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress)
        {
            Listener.Write(nameof(SessionStart), new SessionStart(siloAddress));
        }
    }

    internal static void EmitSessionStop(SiloAddress siloAddress, string reason, int totalCycles)
    {
        if (!Listener.IsEnabled(nameof(SessionStop)))
        {
            return;
        }

        Emit(siloAddress, reason, totalCycles);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress, string reason, int totalCycles)
        {
            Listener.Write(nameof(SessionStop), new SessionStop(
                siloAddress,
                reason,
                totalCycles));
        }
    }

    private sealed class Observable : IObservable<RebalancerEvent>
    {
        public IDisposable Subscribe(IObserver<RebalancerEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<RebalancerEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is RebalancerEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
