using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Core.Diagnostics;

/// <summary>
/// 为 Orleans 会员活动提供诊断听众和事件有效载荷类型。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
internal static class MembershipEvents
{
    /// <summary>
    /// 会员活动诊断听众的姓名。
    /// </summary>
    public const string ListenerName = "Orleans.Membership";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有会员活动的可观测序列。
    /// </summary>
    public static IObservable<MembershipEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于成员诊断活动的基本等级。
    /// </summary>
    public abstract class MembershipEvent
    {
    }

    /// <summary>
    /// 当会籍视图改变时的事件有效载荷 。
    /// </summary>
    /// <param name="snapshot">新的成员快照。</param>
    /// <param name="observerSiloAddress">大会主席的地址 silo 也注意到这一变化。</param>
    public sealed class ViewChanged(
        MembershipTableSnapshot snapshot,
        SiloAddress? observerSiloAddress) : MembershipEvent
    {
        /// <summary>
        /// 新的成员快照。
        /// </summary>
        public readonly MembershipTableSnapshot Snapshot = snapshot;

        /// <summary>
        /// 观察到这一变化的 silo 地址。
        /// </summary>
        public readonly SiloAddress? ObserverSiloAddress = observerSiloAddress;
    }

    /// <summary>
    /// 嫌疑犯或杀人请求的类型
    /// </summary>
    public enum SuspectOrKillRequestType
    {
        /// <summary>
        /// 未知的请求类型 。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 要求怀疑 silo 或宣布其死亡。
        /// </summary>
        SuspectOrKill,

        /// <summary>
        /// 请求宣布 silo 死亡。
        /// </summary>
        Kill
    }

    /// <summary>
    /// 当嫌疑人或杀人请求完成时, 事件有效载荷。
    /// </summary>
    /// <param name="observerSiloAddress">下 silo 处理过请求。</param>
    /// <param name="siloAddress">下 silo 请求所针对的对象。</param>
    /// <param name="otherSiloAddress">与请求有关的其他 silo ,如果有的话。</param>
    /// <param name="requestType">请求类型。</param>
    /// <param name="success">请求是否成功完成 。</param>
    /// <param name="exception">请求提出的例外,如果有的话。</param>
    public sealed class SuspectOrKillRequestCompleted(
        SiloAddress observerSiloAddress,
        SiloAddress siloAddress,
        SiloAddress? otherSiloAddress,
        SuspectOrKillRequestType requestType,
        bool success,
        Exception? exception) : MembershipEvent
    {
        /// <summary>
        /// 处理请求的 silo 。
        /// </summary>
        public readonly SiloAddress ObserverSiloAddress = observerSiloAddress;

        /// <summary>
        /// 请求所针对的 silo 。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 另一 silo 与请求有关联(如果有的话)。
        /// </summary>
        public readonly SiloAddress? OtherSiloAddress = otherSiloAddress;

        /// <summary>
        /// 请求类型。
        /// </summary>
        public readonly SuspectOrKillRequestType RequestType = requestType;

        /// <summary>
        /// 请求是否成功完成 。
        /// </summary>
        public readonly bool Success = success;

        /// <summary>
        /// 请求提出的例外,如果有的话。
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
