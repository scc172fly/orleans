using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Diagnostics;

/// <summary>
/// 提供部署负载发布器统计事件的诊断监听器及事件数据类型。
/// </summary>
/// <remarks>
/// 这些类型是公共类型，但可能在次版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </remarks>
public static class DeploymentLoadPublisherEvents
{
    /// <summary>
    /// 部署负载发布器统计事件的诊断监听器名称。
    /// </summary>
    public const string ListenerName = "Orleans.DeploymentLoadPublisher";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// 获取所有部署负载发布器事件的可观察序列。
    /// </summary>
    public static IObservable<DeploymentLoadPublisherEvent> AllEvents { get; } = new Observable();

    /// <summary>
    /// 部署负载发布器诊断事件使用的基类。
    /// </summary>
    public abstract class DeploymentLoadPublisherEvent
    {
    }

    /// <summary>
    /// Silo 向集群发布其运行时统计信息时的事件数据。
    /// </summary>
    /// <param name="siloAddress">发布统计信息的 Silo 地址。</param>
    /// <param name="statistics">已发布的运行时统计信息。</param>
    public sealed class Published(
        SiloAddress siloAddress,
        SiloRuntimeStatistics statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 发布统计信息的 Silo 地址。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 已发布的运行时统计信息。
        /// </summary>
        public readonly SiloRuntimeStatistics Statistics = statistics;
    }

    /// <summary>
    /// Silo 从另一个 Silo 接收运行时统计信息时的事件数据。
    /// </summary>
    /// <param name="fromSilo">发送统计信息的 Silo 地址。</param>
    /// <param name="receiverSilo">接收统计信息的 Silo 地址。</param>
    /// <param name="statistics">收到的运行时统计信息。</param>
    public sealed class Received(
        SiloAddress fromSilo,
        SiloAddress receiverSilo,
        SiloRuntimeStatistics statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 发送统计信息的 Silo 地址。
        /// </summary>
        public readonly SiloAddress FromSilo = fromSilo;

        /// <summary>
        /// 接收统计信息的 Silo 地址。
        /// </summary>
        public readonly SiloAddress ReceiverSilo = receiverSilo;

        /// <summary>
        /// 收到的运行时统计信息。
        /// </summary>
        public readonly SiloRuntimeStatistics Statistics = statistics;
    }

    /// <summary>
    /// Silo 完成刷新所有集群成员的统计信息时的事件数据。
    /// </summary>
    /// <param name="siloAddress">完成刷新的 Silo 地址。</param>
    /// <param name="statistics">当前缓存的集群统计信息。</param>
    public sealed class ClusterRefreshed(
        SiloAddress siloAddress,
        IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> statistics) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 完成刷新的 Silo 地址。
        /// </summary>
        public readonly SiloAddress SiloAddress = siloAddress;

        /// <summary>
        /// 当前缓存的集群统计信息。
        /// </summary>
        public readonly IReadOnlyDictionary<SiloAddress, SiloRuntimeStatistics> Statistics = statistics;
    }

    /// <summary>
    /// 某个 Silo 的统计信息从缓存中移除时的事件数据。
    /// </summary>
    /// <param name="removedSilo">其统计信息被移除的 Silo 地址。</param>
    /// <param name="observerSilo">执行统计信息移除操作的 Silo 地址。</param>
    public sealed class Removed(
        SiloAddress removedSilo,
        SiloAddress observerSilo) : DeploymentLoadPublisherEvent
    {
        /// <summary>
        /// 其统计信息被移除的 Silo 地址。
        /// </summary>
        public readonly SiloAddress RemovedSilo = removedSilo;

        /// <summary>
        /// 执行统计信息移除操作的 Silo 地址。
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
