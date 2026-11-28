using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供 Orleans 激活实例再均衡器事件的诊断监听器及事件数据类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但可能在次版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class ActivationRebalancerEvents
{
    /// <summary>
    /// 激活实例再均衡器事件的诊断监听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.ActivationRebalancer";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有激活实例再均衡器事件的可观察序列。
    /// </summary>
    public static IObservable<RebalancerEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 激活实例再均衡器诊断事件使用的基类。
    /// </summary>
    public abstract class RebalancerEvent(SiloAddress siloAddress)
    {
        /// <summary>
        /// 承载再均衡器的 Silo 的地址。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;
    }

    /// <summary>
    /// 再均衡轮次开始时的事件数据。
    /// </summary>
    /// <param name="siloAddress">承载再均衡器的 Silo 地址。</param>
    /// <param name="cycleNumber">当前会话中的轮次编号。</param>
    public sealed class CycleStart(
        SiloAddress siloAddress,
        int cycleNumber) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 当前会话中的轮次编号。
        /// </summary>
        public readonly int CycleNumber = cycleNumber;
    }

    /// <summary>
    /// 再均衡轮次完成时的事件数据。
    /// </summary>
    /// <param name="siloAddress">承载再均衡器的 Silo 地址。</param>
    /// <param name="cycleNumber">当前会话中的轮次编号。</param>
    /// <param name="activationsMigrated">该轮次迁移的激活实例数量。</param>
    /// <param name="entropyDeviation">该轮次结束后的熵偏差。</param>
    /// <param name="elapsed">完成该轮次所用的时间。</param>
    /// <param name="sessionCompleted">该轮次是否使会话完成。</param>
    public sealed class CycleStop(
        SiloAddress siloAddress,
        int cycleNumber,
        int activationsMigrated,
        double entropyDeviation,
        TimeSpan elapsed,
        bool sessionCompleted) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 当前会话中的轮次编号。
        /// </summary>
        public readonly int CycleNumber = cycleNumber;

        /// <summary>
        /// 该轮次迁移的激活实例数量。
        /// </summary>
        public readonly int ActivationsMigrated = activationsMigrated;

        /// <summary>
        /// 该轮次结束后的熵偏差。
        /// </summary>
        public readonly double EntropyDeviation = entropyDeviation;

        /// <summary>
        /// 完成该轮次所用的时间。
        /// </summary>
        public readonly TimeSpan Elapsed = elapsed;

        /// <summary>
        /// 指示该轮次是否使会话完成。
        /// </summary>
        public readonly bool SessionCompleted = sessionCompleted;
    }

    /// <summary>
    /// 再均衡会话开始时的事件数据。
    /// </summary>
    /// <param name="siloAddress">承载再均衡器的 Silo 地址。</param>
    public sealed class SessionStart(SiloAddress siloAddress) : RebalancerEvent(siloAddress)
    {
    }

    /// <summary>
    /// 再均衡会话停止时的事件数据。
    /// </summary>
    /// <param name="siloAddress">承载再均衡器的 Silo 地址。</param>
    /// <param name="reason">会话停止的原因。</param>
    /// <param name="totalCycles">会话中已完成的轮次总数。</param>
    public sealed class SessionStop(
        SiloAddress siloAddress,
        string reason,
        int totalCycles) : RebalancerEvent(siloAddress)
    {
        /// <summary>
        /// 会话停止的原因。
        /// </summary>
        public readonly string Reason = reason;

        /// <summary>
        /// 会话中已完成的轮次总数。
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
