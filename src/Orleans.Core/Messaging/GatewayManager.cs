using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using static Orleans.Internal.StandardExtensions;

namespace Orleans.Messaging
{
    /// <summary>
    /// GatewayManager 类持有已知网关列表，同时维护“失效”网关列表。
    ///
    /// 已知网关列表有两种来源：完整列表可能出现在客户端配置对象中，或者
    /// 配置对象可能包含 IGatewayListProvider 委托。如果两者都存在，则委托优先。
    /// </summary>
    internal partial class GatewayManager : IDisposable
    {
        private readonly Lock lockable = new();
        private readonly Dictionary<SiloAddress, DateTime> knownDead = new Dictionary<SiloAddress, DateTime>();
        private readonly Dictionary<SiloAddress, DateTime> knownMasked = new Dictionary<SiloAddress, DateTime>();
        private readonly IGatewayListProvider gatewayListProvider;
        private readonly ILogger logger;
        private readonly ConnectionManager connectionManager;
        private readonly GatewayOptions gatewayOptions;
        private readonly PeriodicTimer gatewayRefreshTimer;
        private readonly CancellationTokenSource shutdown = new();
        private readonly CancellationToken shutdownToken;
        private List<SiloAddress> cachedLiveGateways = [];
        private HashSet<SiloAddress> cachedLiveGatewaysSet = [];
        private List<SiloAddress> knownGateways = [];
        private DateTime lastRefreshTime;
        private int roundRobinCounter;
        private bool gatewayRefreshCallInitiated;
        private bool gatewayListProviderInitialized;
        private Task? gatewayRefreshTimerTask;

        public GatewayManager(
            IOptions<GatewayOptions> gatewayOptions,
            IGatewayListProvider gatewayListProvider,
            ILoggerFactory loggerFactory,
            ConnectionManager connectionManager,
            [FromKeyedServices(TimeProviderNames.Messaging)] TimeProvider timeProvider)
        {
            // 刷新延续操作可能在取消令牌源已释放后观察到关闭状态。
            this.shutdownToken = this.shutdown.Token;
            this.gatewayOptions = gatewayOptions.Value;
            this.logger = loggerFactory.CreateLogger<GatewayManager>();
            this.connectionManager = connectionManager;
            this.gatewayListProvider = gatewayListProvider;

            var refreshPeriod = Max(this.gatewayOptions.GatewayListRefreshPeriod, TimeSpan.FromMilliseconds(1));
            this.gatewayRefreshTimer = new PeriodicTimer(this.gatewayOptions.GatewayListRefreshPeriod, timeProvider);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!gatewayListProviderInitialized)
            {
                await this.gatewayListProvider.InitializeGatewayListProviderAsync(cancellationToken);
                gatewayListProviderInitialized = true;
            }

            var knownGateways = await this.gatewayListProvider.GetGatewaysAsync(cancellationToken);
            if (knownGateways == null || knownGateways.Count == 0)
            {
                // 如果客户端比 Silo 更快启动，就可能出现这种情况。
                var providerName = this.gatewayListProvider.GetType().FullName;
                LogNoGatewayDuringInitialization(this.logger, providerName);
                var message = $"Could not find any gateway in '{providerName}'. Orleans client cannot initialize until at least one gateway becomes available.";
                throw new SiloUnavailableException(message);
            }

            LogFoundGateways(this.logger, knownGateways.Count, new(knownGateways));

            this.roundRobinCounter = this.gatewayOptions.PreferredGatewayIndex >= 0 ? this.gatewayOptions.PreferredGatewayIndex : Random.Shared.Next(knownGateways.Count);
            var newGateways = new List<SiloAddress>();
            foreach (var gatewayUri in knownGateways)
            {
                if (gatewayUri?.ToGatewayAddress() is { } gatewayAddress)
                {
                    newGateways.Add(gatewayAddress);
                }
            }

            this.knownGateways = this.cachedLiveGateways = newGateways;
            this.cachedLiveGatewaysSet = new HashSet<SiloAddress>(cachedLiveGateways);
            this.lastRefreshTime = DateTime.UtcNow;
            this.gatewayRefreshTimerTask ??= PeriodicallyRefreshGatewaySnapshot();
            EmitGatewayListUpdatedIfEnabled(this.knownGateways, this.cachedLiveGateways);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!shutdownToken.IsCancellationRequested)
            {
                await shutdown.CancelAsync();
            }

            gatewayRefreshTimer.Dispose();
            if (gatewayRefreshTimerTask is { } task)
            {
                await task.WaitAsync(cancellationToken);
            }
        }

        public void MarkAsDead(SiloAddress gateway)
        {
            lock (lockable)
            {
                knownDead[gateway] = DateTime.UtcNow;
                var copy = new List<SiloAddress>(cachedLiveGateways);
                copy.Remove(gateway);
                // 交换引用而不修改 cachedLiveGateways，使我们能够不持锁访问 cachedLiveGateways。
                cachedLiveGateways = copy;
                cachedLiveGatewaysSet = new HashSet<SiloAddress>(cachedLiveGateways);
            }
        }

        public void MarkAsUnavailableForSend(SiloAddress gateway)
        {
            lock (lockable)
            {
                knownMasked[gateway] = DateTime.UtcNow;
                var copy = new List<SiloAddress>(cachedLiveGateways);
                copy.Remove(gateway);
                // 交换引用而不修改 cachedLiveGateways，使我们能够不持锁访问 cachedLiveGateways。
                cachedLiveGateways = copy;
                cachedLiveGatewaysSet = new HashSet<SiloAddress>(cachedLiveGateways);
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("GatewayManager: ");
            lock (lockable)
            {
                if (cachedLiveGateways != null)
                {
                    sb.Append(cachedLiveGateways.Count);
                    sb.Append(" cachedLiveGateways, ");
                }
                if (knownDead != null)
                {
                    sb.Append(knownDead.Count);
                    sb.Append(" known dead gateways.");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 选择供新桶使用的网关。
        ///
        /// 注意：如果提供了列表提供程序委托，则每次调用此方法都会调用该委托。
        /// 此方法执行缓存，以避免频繁访问最终的数据源。
        ///
        /// 此实现采用简单的轮询选择。它假定提供程序返回的网关列表
        /// 每次都按相同顺序排列。
        /// </summary>
        /// <returns></returns>
        public SiloAddress? GetLiveGateway()
        {
            List<SiloAddress> live = GetLiveGateways();
            int count = live.Count;
            if (count > 0)
            {
                lock (lockable)
                {
                    // 从上次停止的位置开始，轮询已知网关并选择下一个有效网关。
                    roundRobinCounter = (roundRobinCounter + 1) % count;
                    return live[roundRobinCounter];
                }
            }
            // 如果执行到此处，则假定所有已知网关均已失效。
            return null;
        }


        public List<SiloAddress> GetLiveGateways()
        {
            // 从不获取锁，直接快速返回 cachedLiveGateways 列表，不执行其他操作。
            // 仅当列表为空时，才异步启动网关刷新。
            if (cachedLiveGateways.Count == 0)
            {
                ExpediteUpdateLiveGatewaysSnapshot();

                if (knownGateways.Count > 0)
                {
                    lock (this.lockable)
                    {
                        if (cachedLiveGateways.Count == 0 && knownGateways.Count > 0)
                        {
                            LogAllGatewaysMarkedDead(this.logger);

                            cachedLiveGateways = knownGateways;
                            cachedLiveGatewaysSet = new HashSet<SiloAddress>(knownGateways);
                        }
                    }
                }
            }

            return cachedLiveGateways;
        }

        public bool IsGatewayAvailable(SiloAddress siloAddress)
        {
            return cachedLiveGatewaysSet.Contains(siloAddress);
        }

        internal void ExpediteUpdateLiveGatewaysSnapshot()
        {
            // 如果已有一次加急刷新调用正在进行，则在前一次完成前不要再次调用。
            // 我们不希望发出过多网关刷新调用。
            if (gatewayListProvider == null || gatewayRefreshCallInitiated) return;

            // 异步启动网关列表刷新。无论如何，刷新定时器仍会继续触发。
            // 我们不希望同步刷新调用阻塞客户端。
            // 客户端调用将因“No Gateways found”失败，但我们会尝试尽快刷新列表。
            gatewayRefreshCallInitiated = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshGatewaySnapshot();
                }
                finally
                {
                    gatewayRefreshCallInitiated = false;
                }
            });
        }

        internal async Task PeriodicallyRefreshGatewaySnapshot()
        {
            await Task.Yield();

            if (gatewayListProvider is null)
            {
                return;
            }

            while (await gatewayRefreshTimer.WaitForNextTickAsync())
            {
                await RefreshGatewaySnapshot();
            }
        }

        private async Task RefreshGatewaySnapshot()
        {
            try
            {
                if (gatewayListProvider is null)
                {
                    return;
                }

                // listProvider.GetGateways() 不在锁内执行。
                shutdownToken.ThrowIfCancellationRequested();
                var allGateways = await gatewayListProvider.GetGatewaysAsync(shutdownToken);
                var refreshedGateways = allGateways.Select(gw => gw.ToGatewayAddress()!).ToList();

                await UpdateLiveGatewaysSnapshot(refreshedGateways, gatewayListProvider.MaxStaleness);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // 网关提供程序拥有其查找操作；关闭时解除此管理器对刷新操作的等待。
            }
            catch (Exception exc)
            {
                LogErrorRefreshingGateways(this.logger, exc);
            }
        }

        // 此函数由网关刷新定时器异步调用。
        private async Task UpdateLiveGatewaysSnapshot(IEnumerable<SiloAddress> refreshedGateways, TimeSpan maxStaleness)
        {
            List<SiloAddress> connectionsToKeepAlive;
            List<SiloAddress> knownGatewaysSnapshotSource;
            List<SiloAddress> liveGatewaysSnapshotSource;

            // 此锁的持有时间很短，用于保护对 knownDead、knownMasked 和 cachedLiveGateways 的访问。
            lock (lockable)
            {
                // 现在获取 listProvider 提供的列表，并排除我们认为已失效的网关。
                var live = new List<SiloAddress>();
                var now = DateTime.UtcNow;

                this.knownGateways = refreshedGateways as List<SiloAddress> ?? refreshedGateways.ToList();
                foreach (SiloAddress trial in knownGateways)
                {
                    var address = trial.Generation == 0 ? trial : SiloAddress.New(trial.Endpoint, 0);

                    // 如果因套接字错误将某节点记录为失效，且
                    // 记录时间（diedAt）距今尚未达到 maxStaleness，则将该节点视为失效。
                    // 后一个条件用于处理网关提供程序返回过时列表的情况，该列表尚未反映实际刚失效的网关。
                    // 如果已超过 maxStaleness，则假设 maxStaleness 是网关提供程序数据陈旧程度的上限。
                    var isDead = false;
                    if (knownDead.TryGetValue(address, out var diedAt))
                    {
                        if (now.Subtract(diedAt) < maxStaleness)
                        {
                            isDead = true;
                        }
                        else
                        {
                            // 移除陈旧条目。
                            knownDead.Remove(address);
                        }
                    }
                    if (knownMasked.TryGetValue(address, out var maskedAt))
                    {
                        if (now.Subtract(maskedAt) < maxStaleness)
                        {
                            isDead = true;
                        }
                        else
                        {
                            // 移除陈旧条目。
                            knownMasked.Remove(address);
                        }
                    }

                    if (!isDead)
                    {
                        live.Add(address);
                    }
                }

                if (live.Count == 0)
                {
                    LogAllGatewaysDead(logger);
                    live.AddRange(knownGateways);
                    knownDead.Clear();
                }

                // 在一个原子操作中交换 cachedLiveGateways 引用。
                cachedLiveGateways = live;
                cachedLiveGatewaysSet = new HashSet<SiloAddress>(live);
                knownGatewaysSnapshotSource = knownGateways;
                liveGatewaysSnapshotSource = live;

                DateTime prevRefresh = lastRefreshTime;
                lastRefreshTime = now;
                LogRefreshedLiveGatewayList(logger, knownGateways.Count, new(knownGateways), cachedLiveGateways.Count, new(cachedLiveGateways), prevRefresh);

                // 关闭已知失效的连接，但保留“已屏蔽”的连接。
                // 客户端不会向“已屏蔽”的连接发送任何新请求，但仍可能
                // 接收响应。
                connectionsToKeepAlive = new List<SiloAddress>(live);
                connectionsToKeepAlive.AddRange(knownMasked.Select(e => e.Key));
            }

            EmitGatewayListUpdatedIfEnabled(knownGatewaysSnapshotSource, liveGatewaysSnapshotSource);
            await this.CloseEvictedGatewayConnections(connectionsToKeepAlive);
        }

        private void EmitGatewayListUpdatedIfEnabled(List<SiloAddress> knownGatewaysSnapshotSource, List<SiloAddress> liveGatewaysSnapshotSource)
        {
            if (GatewayEvents.IsGatewayListUpdatedEnabled())
            {
                GatewayEvents.EmitGatewayListUpdated(this, knownGatewaysSnapshotSource.ToArray(), liveGatewaysSnapshotSource.ToArray());
            }
        }

        private async Task CloseEvictedGatewayConnections(List<SiloAddress> liveGateways)
        {
            if (this.connectionManager == null) return;

            var connectedGateways = this.connectionManager.GetConnectedAddresses();
            foreach (var address in connectedGateways)
            {
                var isLiveGateway = false;
                foreach (var live in liveGateways)
                {
                    if (live.Matches(address))
                    {
                        isLiveGateway = true;
                        break;
                    }
                }

                if (!isLiveGateway)
                {
                    LogClosingConnectionToDeadGateway(this.logger, address);

                    await this.connectionManager.CloseAsync(address);
                }
            }
        }

        public void Dispose()
        {
            if (!shutdownToken.IsCancellationRequested)
            {
                shutdown.Cancel();
            }

            this.gatewayRefreshTimer.Dispose();
            shutdown.Dispose();
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayManager_NoGateways,
            Level = LogLevel.Warning,
            Message = "Could not find any gateway in '{GatewayListProviderName}'. Orleans client cannot initialize until at least one gateway becomes available."
        )]
        private static partial void LogNoGatewayDuringInitialization(ILogger logger, string? gatewayListProviderName);

        private readonly struct UrisLogValue(IList<Uri> uris)
        {
            public override readonly string ToString() => Utils.EnumerableToString(uris);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayManager_FoundKnownGateways,
            Level = LogLevel.Information,
            Message = "Found '{GatewayCount}' gateways: '{Gateways}'."
        )]
        private static partial void LogFoundGateways(ILogger logger, int gatewayCount, UrisLogValue gateways);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "All known gateways have been marked dead locally. Expediting gateway refresh and resetting all gateways to live status."
        )]
        private static partial void LogAllGatewaysMarkedDead(ILogger logger);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_GetGateways,
            Level = LogLevel.Error,
            Message = "Error refreshing gateways."
        )]
        private static partial void LogErrorRefreshingGateways(ILogger logger, Exception exc);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Closing connection to '{EndPoint}' because it has been marked as dead."
        )]
        private static partial void LogClosingConnectionToDeadGateway(ILogger logger, SiloAddress endPoint);

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayManager_AllGatewaysDead,
            Level = LogLevel.Warning,
            Message = "All gateways have previously been marked as dead. Clearing the list of dead gateways to expedite reconnection."
        )]
        private static partial void LogAllGatewaysDead(ILogger logger);

        private readonly struct SiloAddressesLogValue(List<SiloAddress> addresses)
        {
            public override string ToString() => Utils.EnumerableToString(addresses);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayManager_FoundKnownGateways,
            Level = LogLevel.Debug,
            Message = "Refreshed the live gateway list. Found '{KnownGatewayCount}' gateways from gateway list provider: '{KnownGateways}'. Picked only known live out of them. Now has '{LiveGatewayCount}' live gateways: '{LiveGateways}'. Previous refresh time was = '{PreviousRefreshTime}'"
        )]
        private static partial void LogRefreshedLiveGatewayList(ILogger logger, int knownGatewayCount, SiloAddressesLogValue knownGateways, int liveGatewayCount, SiloAddressesLogValue liveGateways, DateTime previousRefreshTime);
    }
}
