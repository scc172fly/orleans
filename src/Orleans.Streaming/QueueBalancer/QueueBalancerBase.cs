using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Runtime.Internal;

namespace Orleans.Streams
{
    /// <summary>
    /// StreamQueueBalancer的基准类
    /// </summary>
    public abstract partial class QueueBalancerBase : IStreamQueueBalancer
    {
        private readonly IAsyncEnumerable<ClusterMembershipSnapshot> clusterMembershipUpdates;
        private readonly List<IStreamQueueBalanceListener> queueBalanceListeners;
        private readonly CancellationTokenSource cts;
        private Task _listenForClusterChangesTask = null!; // 初始化时开始。

        /// <summary>
        /// 获取当此平衡器关闭时所信号的取消符。
        /// </summary>
        protected CancellationToken Cancellation => this.cts.Token;

        /// <summary>
        /// 获取本地 silo的地址。
        /// </summary>
        protected SiloAddress SiloAddress { get; }

        /// <summary>
        /// 得到伐木者。
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// 初始化 <see cref="QueueBalancerBase"/> 类的新实例。
        /// </summary>
        /// <param name="sp">服务供应商。</param>
        /// <param name="logger">日志器。</param>
        protected QueueBalancerBase(IServiceProvider sp, ILogger logger)
            : this(sp.GetRequiredService<IClusterMembershipService>(), sp.GetRequiredService<ILocalSiloDetails>(), logger)
        {
        }

        /// <summary>
        /// 应在 IAsyncEnumerable 一旦释放后成为主建建筑工
        /// </summary>
        private QueueBalancerBase(IClusterMembershipService clusterMembership, ILocalSiloDetails localSiloDetails, ILogger logger)
        {
            this.clusterMembershipUpdates = clusterMembership.MembershipUpdates;
            this.SiloAddress = localSiloDetails.SiloAddress;
            this.Logger = logger;
            this.queueBalanceListeners = new List<IStreamQueueBalanceListener>();
            this.cts = new CancellationTokenSource();
        }

        /// <inheritdoc/>
        public abstract IEnumerable<QueueId> GetMyQueues();

        /// <inheritdoc/>
        public virtual Task Initialize(IStreamQueueMapper queueMapper)
        {
            using var _ = new ExecutionContextSuppressor();
            _listenForClusterChangesTask = ListenForClusterChanges();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method",
            Justification = "Shutdown invokes cancellation callbacks on the calling scheduler before joining the membership worker.")]
        public virtual async Task Shutdown()
        {
            try
            {
                this.cts.Cancel(throwOnFirstException: false);
            }
            catch (AggregateException exc)
            {
                LogErrorSignalingShutdownToken(Logger, exc);
            }

            await _listenForClusterChangesTask.SuppressThrowing();
        }

        /// <inheritdoc/>
        public bool SubscribeToQueueDistributionChangeEvents(IStreamQueueBalanceListener observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (this.queueBalanceListeners)
            {
                if (this.queueBalanceListeners.Contains(observer))
                {
                    return false;
                }
                this.queueBalanceListeners.Add(observer);
                return true;
            }
        }
        /// <inheritdoc/>
        public bool UnSubscribeFromQueueDistributionChangeEvents(IStreamQueueBalanceListener observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (this.queueBalanceListeners)
            {
                return this.queueBalanceListeners.Remove(observer);
            }
        }

        /// <summary>
        /// 通知订户队列分布已改变。
        /// </summary>
        /// <returns>一项任务,代表通知操作。</returns>
        protected Task NotifyListeners()
        {
            if (this.Cancellation.IsCancellationRequested) return Task.CompletedTask;
            List<IStreamQueueBalanceListener> queueBalanceListenersCopy;
            lock (queueBalanceListeners)
            {
                queueBalanceListenersCopy = queueBalanceListeners.ToList(); // 复制
            }
            return Task.WhenAll(queueBalanceListenersCopy.Select(listener => listener.QueueDistributionChangeNotification()));
        }

        private async Task ListenForClusterChanges()
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            var current = new HashSet<SiloAddress>();
            await foreach (var membershipSnapshot in this.clusterMembershipUpdates.WithCancellation(this.Cancellation))
            {
                try
                {
                    // 获取活跃成员。
                    var update = new HashSet<SiloAddress>(membershipSnapshot.Members.Values
                        .Where(member => member.Status == SiloStatus.Active)
                        .Select(member => member.SiloAddress));

                    // 如果活动列表已更改, 请跟踪新列表并通知 。
                    if (!current.SetEquals(update))
                    {
                        current = update;
                        OnClusterMembershipChange(current);
                    }
                }
                catch (Exception exception)
                {
                    LogErrorProcessingClusterMembershipUpdate(Logger, exception);
                }
            }
        }

        /// <summary>
        /// 处理对活动 silos的一组修改。
        /// </summary>
        /// <param name="activeSilos">活动 silos的地址。</param>
        protected abstract void OnClusterMembershipChange(HashSet<SiloAddress> activeSilos);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error signaling shutdown token."
        )]
        private static partial void LogErrorSignalingShutdownToken(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error processing cluster membership update."
        )]
        private static partial void LogErrorProcessingClusterMembershipUpdate(ILogger logger, Exception exception);
    }
}
