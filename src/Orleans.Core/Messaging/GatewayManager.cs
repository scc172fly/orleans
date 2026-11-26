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
    /// GatewayManager 类持有已知网关清单,并保留“死”网关清单。
    ///
    /// 已知列表可以来自两个位子之一:完整列表可以在客户配置对象中出现,或
    /// 配置对象中可包含一个 IGatewayListProvider 代表。如果两者都出现,该代表优先。
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
            // 刷新后可以观察源处理完后关闭的情况。
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
                // 如果客户端启动速度快于 silos,这种情况就可能发生。
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
                // 将引用替换为已引用, 不要变换 cachedLiveGateways, 这样我们就可以在没有锁的情况下访问 cachedLiveGateways 。
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
                // 将引用替换为已引用, 不要变换 cachedLiveGateways, 这样我们就可以在没有锁的情况下访问 cachedLiveGateways 。
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
        /// 选择用于新桶的通道。
        ///
        /// 请注意,如果给代表名单提供人,每次调用该方法时都援引该代表。
        /// 此方法进行缓存, 以避免敲敲最终数据源 。
        ///
        /// 执行此操作进行简单的圆轮Robin选择。 它假设供应商提供的网关列表
        /// 每次都按同一顺序排列。
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
                    // 绕环环通过已知的网关 并取下一个直播的, 从我们最后离开的地方开始
                    roundRobinCounter = (roundRobinCounter + 1) % count;
                    return live[roundRobinCounter];
                }
            }
            // 如果我们通过,所有已知的入口 都假定死亡
            return null;
        }


        public List<SiloAddress> GetLiveGateways()
        {
            // 永远不拿上锁, 不操作便快速返回 cachedLiveGateways 列表 。
            // 仅在网关空时才开始自动刷新。
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
            // 如果已经快速更新电话, 不要再打电话, 直到上一个电话完成 。
            // 我们不想发出太多的 网关刷新电话。
            if (gatewayListProvider == null || gatewayRefreshCallInitiated) return;

            // 启动网关列表时会同步刷新。 不管是否选中, 刷新计时器都会一直继续 。
            // 我们不想用同步的更新电话堵住客户
            // 客户端的电话会因“找不到网关”而失败,
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

                // listProvider.GetGateways() 不锁定。
                shutdownToken.ThrowIfCancellationRequested();
                var allGateways = await gatewayListProvider.GetGatewaysAsync(shutdownToken);
                var refreshedGateways = allGateways.Select(gw => gw.ToGatewayAddress()!).ToList();

                await UpdateLiveGatewaysSnapshot(refreshedGateways, gatewayListProvider.MaxStaleness);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // 网关供应商拥有自己的搜索功能;关闭释放了经理的清新等待。
            }
            catch (Exception exc)
            {
                LogErrorRefreshingGateways(this.logger, exc);
            }
        }

        // 函数从网关刷新计时器中自动命名。
        private async Task UpdateLiveGatewaysSnapshot(IEnumerable<SiloAddress> refreshedGateways, TimeSpan maxStaleness)
        {
            List<SiloAddress> connectionsToKeepAlive;
            List<SiloAddress> knownGatewaysSnapshotSource;
            List<SiloAddress> liveGatewaysSnapshotSource;

            // 这是一个短锁, 保护访问 knownDead、 knownMasked 和 cachedLiveGateways的通道。
            lock (lockable)
            {
                // 现在拿什么 listProvider 并排除了那些我们认为已经死亡的人
                var live = new List<SiloAddress>();
                var now = DateTime.UtcNow;

                this.knownGateways = refreshedGateways as List<SiloAddress> ?? refreshedGateways.ToList();
                foreach (SiloAddress trial in knownGateways)
                {
                    var address = trial.Generation == 0 ? trial : SiloAddress.New(trial.Endpoint, 0);

                    // 如果记录了一个节点,我们视它因插接错误而死亡, 结点即为死
                    // 记录它,diedAt)不久之前(少于 maxStaleness )))前,
                    // 后者将涵盖当网关提供者退回一个尚未反映最近实际死亡的网关过时名单时的情况。
                    // 如果超过 maxStaleness ——我们假设 maxStaleness 是网关供应商新鲜度的上限。
                    var isDead = false;
                    if (knownDead.TryGetValue(address, out var diedAt))
                    {
                        if (now.Subtract(diedAt) < maxStaleness)
                        {
                            isDead = true;
                        }
                        else
                        {
                            // 删除删除的条目 。
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
                            // 删除删除的条目 。
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

                // 以一个原子操作中交换 cachedLiveGateways 指针
                cachedLiveGateways = live;
                cachedLiveGatewaysSet = new HashSet<SiloAddress>(live);
                knownGatewaysSnapshotSource = knownGateways;
                liveGatewaysSnapshotSource = live;

                DateTime prevRefresh = lastRefreshTime;
                lastRefreshTime = now;
                LogRefreshedLiveGatewayList(logger, knownGateways.Count, new(knownGateways), cachedLiveGateways.Count, new(cachedLiveGateways), prevRefresh);

                // 与已知的死线连接很近 但保持"假面"连接
                // 客户端不会向“ 假装” 连接发送任何新请求, 但可能仍然
                // 收到的答复
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
