using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供诊断听者类型和事件有效载荷类型,用于部署负载出版社统计活动。
/// </summary>
/// <remarks>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断等先进假设情景。
/// </remarks>
public static class DeploymentLoadPublisherEvents
{
    /// <summary>
    /// 部署负载出版商统计事件诊断听众的名字。
    /// </summary>
    public const string ListenerName = "Orleans.DeploymentLoadPublisher";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获得所有部署负载出版商事件的可观测序列。
    /// </summary>
    public static IObservable<DeploymentLoadPublisherEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 用于部署装货量出版商诊断事件的基本等级。
    /// </summary>
    public abstract class DeploymentLoadPublisherEvent
    {
    }

    /// <summary>
    /// 事件有效载荷,用于 silo 向集发布运行时统计数据。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo 出版统计。</param>
    /// <param name="statistics">公布的运行时统计。</param>
    public sealed class Published(
        SiloAddress siloAddress,
        SiloRuntimeStatistics statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 出版统计 silo 的地址。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 公布的运行时间统计数据。
        /// </summary>
        public readonly SiloRuntimeStatistics Statistics = statistics;
    }

    /// <summary>
    /// silo 从另一个 silo获得运行时统计时,事件有效载荷。
    /// </summary>
    /// <param name="fromSilo">大会主席的地址 silo 发送统计的那个人。</param>
    /// <param name="receiverSilo">大会主席的地址 silo 收到统计数字的国家。</param>
    /// <param name="statistics">收到的运行时间统计数据。</param>
    public sealed class Received(
        SiloAddress fromSilo,
        SiloAddress receiverSilo,
        SiloRuntimeStatistics statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 发送统计数据的 silo 地址。
        /// </summary>
        public readonly SiloAddress FromSilo = fromSilo;

        /// <summary>
        /// 收到统计数据的 silo 地址。
        /// </summary>
        public readonly SiloAddress ReceiverSilo = receiverSilo;

        /// <summary>
        /// 收到的运行时间统计数据。
        /// </summary>
        public readonly SiloRuntimeStatistics Statistics = statistics;
    }

    /// <summary>
    /// 当 silo 完成来自所有组群成员的更新统计数据时,事件有效载荷。
    /// </summary>
    /// <param name="siloAddress">填充刷新内容的 silo 地址。</param>
    /// <param name="statistics">当前缓存集统计数据。</param>
    public sealed class ClusterRefreshed(
        SiloAddress siloAddress,
        IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 填充刷新内容的 silo 地址 。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 当前缓存的集束统计 。
        /// </summary>
        public readonly IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> Statistics = statistics;
    }

    /// <summary>
    /// 从缓存中删除 silo统计数据时的事件有效载荷。
    /// </summary>
    /// <param name="removedSilo">大会主席的地址 silo 其统计数据被删除。</param>
    /// <param name="observerSilo">大会主席的地址 silo ,删除了统计数据。</param>
    public sealed class Removed(
        SiloAddress removedSilo,
        SiloAddress observerSilo) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 统计被删除的 silo 地址。
        /// </summary>
        public readonly SiloAddress RemovedSilo = removedSilo;

        /// <summary>
        /// 删除统计数据的 silo 地址。
        /// </summary>
        public readonly SiloAddress ObserverSilo = observerSilo;
    }

    internal static void EmitClusterRefreshed(SiloAddress siloAddress, IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> statistics)
    {
        if (!Listener.IsEnabled(nameof(ClusterRefreshed)))
        {
            return;
        }

        Emit(siloAddress, statistics);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress, IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> statistics)
        {
            Listener.Write(nameof(ClusterRefreshed), new ClusterRefreshed(
                siloAddress,
                statistics));
        }
    }

    internal static void EmitPublished(SiloAddress siloAddress, SiloRuntimeStatistics statistics)
    {
        if (!Listener.IsEnabled(nameof(Published)))
        {
            return;
        }

        Emit(siloAddress, statistics);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress siloAddress, SiloRuntimeStatistics statistics)
        {
            Listener.Write(nameof(Published), new Published(
                siloAddress,
                statistics));
        }
    }

    internal static void EmitReceived(SiloAddress sourceSiloAddress, SiloAddress observerSiloAddress, SiloRuntimeStatistics statistics)
    {
        if (sourceSiloAddress.Equals(observerSiloAddress)
            || !Listener.IsEnabled(nameof(Received)))
        {
            return;
        }

        Emit(sourceSiloAddress, observerSiloAddress, statistics);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress sourceSiloAddress, SiloAddress observerSiloAddress, SiloRuntimeStatistics statistics)
        {
            Listener.Write(nameof(Received), new Received(
                sourceSiloAddress,
                observerSiloAddress,
                statistics));
        }
    }

    internal static void EmitRemoved(SiloAddress removedSiloAddress, SiloAddress observerSiloAddress)
    {
        if (!Listener.IsEnabled(nameof(Removed)))
        {
            return;
        }

        Emit(removedSiloAddress, observerSiloAddress);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(SiloAddress removedSiloAddress, SiloAddress observerSiloAddress)
        {
            Listener.Write(nameof(Removed), new Removed(
                removedSiloAddress,
                observerSiloAddress));
        }
    }

    private sealed class Observable : IObservable<DeploymentLoadPublisherEvent>
    {
        public IDisposable Subscribe(IObserver<DeploymentLoadPublisherEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<DeploymentLoadPublisherEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();
            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is DeploymentLoadPublisherEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
