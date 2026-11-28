using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Core.Diagnostics;

/// <summary>
/// 提供 Orleans 成员资格事件的诊断侦听器和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的，但可能随次要版本更新而变化。它们用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
internal static class MembershipEvents
{
    /// <summary>
    /// 成员资格事件的诊断侦听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.Membership";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取包含所有成员资格事件的可观察序列。
    /// </summary>
    public static IObservable<MembershipEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 成员资格诊断事件所使用的基类。
    /// </summary>
    public abstract class MembershipEvent
    {
    }

    /// <summary>
    /// 成员资格视图发生变化时的事件有效载荷。
    /// </summary>
    /// <param name="snapshot">新的成员资格快照。</param>
    /// <param name="observerSiloAddress">观察到此变更的 Silo 地址。</param>
    public sealed class ViewChanged(
        MembershipTableSnapshot snapshot,
        SiloAddress? observerSiloAddress) : MembershipEvent
    {
        /// <summary>
        /// 新的成员资格快照。
        /// </summary>
        public readonly MembershipTableSnapshot Snapshot = snapshot;

        /// <summary>
        /// 观察到此变更的 Silo 地址。
        /// </summary>
        public readonly SiloAddress? ObserverSiloAddress = observerSiloAddress;
    }

    /// <summary>
    /// 怀疑或宣告死亡请求的类型。
    /// </summary>
    public enum SuspectOrKillRequestType
    {
        /// <summary>
        /// 未知请求类型。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 怀疑某个 Silo 或宣告其死亡的请求。
        /// </summary>
        SuspectOrKill,

        /// <summary>
        /// 宣告某个 Silo 死亡的请求。
        /// </summary>
        Kill
    }

    /// <summary>
    /// 怀疑或宣告死亡请求完成时的事件有效载荷。
    /// </summary>
    /// <param name="observerSiloAddress">处理该请求的 Silo。</param>
    /// <param name="siloAddress">请求所针对的 Silo。</param>
    /// <param name="otherSiloAddress">与请求关联的另一个 Silo（如果存在）。</param>
    /// <param name="requestType">请求类型。</param>
    /// <param name="success">请求是否成功完成。</param>
    /// <param name="exception">请求抛出的异常（如果存在）。</param>
    public sealed class SuspectOrKillRequestCompleted(
        SiloAddress observerSiloAddress,
        SiloAddress siloAddress,
        SiloAddress? otherSiloAddress,
        SuspectOrKillRequestType requestType,
        bool success,
        Exception? exception) : MembershipEvent
    {
        /// <summary>
        /// 处理该请求的 Silo。
        /// </summary>
        public readonly SiloAddress ObserverSiloAddress = observerSiloAddress;

        /// <summary>
        /// 请求所针对的 Silo。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 与请求关联的另一个 Silo（如果存在）。
        /// </summary>
        public readonly SiloAddress? OtherSiloAddress = otherSiloAddress;

        /// <summary>
        /// 请求类型。
        /// </summary>
        public readonly SuspectOrKillRequestType RequestType = requestType;

        /// <summary>
        /// 请求是否成功完成。
        /// </summary>
        public readonly bool Success = success;

        /// <summary>
        /// 请求抛出的异常（如果存在）。
        /// </summary>
        public readonly Exception? Exception = exception;
    }

    internal static void EmitViewChanged(MembershipTableSnapshot newSnapshot, SiloAddress observerAddress)
    {
        if (!Listener.IsEnabled(nameof(ViewChanged)))
        {
            return;
        }

        Emit(newSnapshot, observerAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(MembershipTableSnapshot newSnapshot, SiloAddress observerAddress)
        {
            Listener.Write(nameof(ViewChanged), new ViewChanged(
                newSnapshot,
                observerAddress));
        }
    }

    internal static void EmitSuspectOrKillRequestCompleted(
        SiloAddress observerSiloAddress,
        SiloAddress siloAddress,
        SiloAddress? otherSiloAddress,
        SuspectOrKillRequestType requestType,
        bool success,
        Exception? exception)
    {
        if (!Listener.IsEnabled(nameof(SuspectOrKillRequestCompleted)))
        {
            return;
        }

        Emit(observerSiloAddress, siloAddress, otherSiloAddress, requestType, success, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(
            SiloAddress observerSiloAddress,
            SiloAddress siloAddress,
            SiloAddress? otherSiloAddress,
            SuspectOrKillRequestType requestType,
            bool success,
            Exception? exception)
        {
            Listener.Write(nameof(SuspectOrKillRequestCompleted), new SuspectOrKillRequestCompleted(
                observerSiloAddress,
                siloAddress,
                otherSiloAddress,
                requestType,
                success,
                exception));
        }
    }

    private sealed class Observable : IObservable<MembershipEvent>
    {
        public IDisposable Subscribe(IObserver<MembershipEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<MembershipEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is MembershipEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
