using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Polly;

namespace Orleans.Runtime.Membership
{
    /// <summary>
    /// 提供 Orleans 网关地址 ZooKeeper 分组成员。
    /// </summary>
    public class ZooKeeperGatewayListProvider : IGatewayListProvider
    {
        private readonly ZooKeeperWatcher _watcher;

        /// <summary>
        /// 此部署的节点名称。 例如 /ClusterId
        /// </summary>
        private readonly string _deploymentPath;

        /// <summary>
        /// “192.168.1.1、192.168.1.2/ClusterId”等的部署连接字符串。
        /// </summary>
        private readonly string _deploymentConnectionString;
        private readonly TimeSpan _maxStaleness;
        private readonly Func<ZooKeeperSession> _createSession;
        private readonly ResiliencePipeline _readRetryPipeline;

        /// <summary>
        /// 初始化 <see cref="ZooKeeperGatewayListProvider"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志器。</param>
        /// <param name="options"> ZooKeeper 网关发现选项。</param>
        /// <param name="gatewayOptions">网关发现刷新选项。</param>
        /// <param name="clusterOptions">集群识别选项。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="logger"/>, <paramref name="options"/>, <paramref name="gatewayOptions"/>,或
        /// <paramref name="clusterOptions"/> 是 <see langword="null"/>。
        /// </exception>
        public ZooKeeperGatewayListProvider(
            ILogger<ZooKeeperGatewayListProvider> logger,
            IOptions<ZooKeeperGatewayListProviderOptions> options,
            IOptions<GatewayOptions> gatewayOptions,
            IOptions<ClusterOptions> clusterOptions)
            : this(logger, options, gatewayOptions, clusterOptions, null, null)
        {
        }

        internal ZooKeeperGatewayListProvider(
            ILogger<ZooKeeperGatewayListProvider> logger,
            IOptions<ZooKeeperGatewayListProviderOptions> options,
            IOptions<GatewayOptions> gatewayOptions,
            IOptions<ClusterOptions> clusterOptions,
            Func<ZooKeeperSession>? createSession,
            ResiliencePipeline? readRetryPipeline)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(gatewayOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);

            _watcher = new ZooKeeperWatcher(logger);
            _deploymentPath = "/" + clusterOptions.Value.ClusterId;
            _deploymentConnectionString = options.Value.ConnectionString + _deploymentPath;
            _maxStaleness = gatewayOptions.Value.GatewayListRefreshPeriod;
            _createSession = createSession ?? (() => ZooKeeperBasedMembershipTable.CreateSession(_deploymentConnectionString, _watcher, true));
            _readRetryPipeline = readRetryPipeline ?? ZooKeeperReadRetryPolicy.CreatePipeline(logger, TimeProvider.System);
        }

        /// <summary>
        /// 初始化基于 ZooKeeper 的网关提供者
        /// </summary>
        public Task InitializeGatewayListProvider() => InitializeGatewayListProviderAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 返回网关列表( 返回) (n)silos客户端可以连接到 Orleans 组。
        /// 尤里语的形式是: "gwy.tcp/IP:port/Generation. " 关于尤里格式的更多详情,见 Utils.ToGatewayUri 和 Utils.ToSiloAddress 。
        /// </summary>
        /// <remarks>
        /// 网关发现使用版本的封隔会员快照。 本地读者读到 retry connect- loss
        /// 操作会话失败最多四次,然后传播最后失败。
        /// </remarks>
        public Task<IList<Uri>> GetGateways() => GetGatewaysAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var membershipTableData = await ZooKeeperBasedMembershipTable.ReadAsync(_createSession, _readRetryPipeline, null, cancellationToken);
            return membershipTableData.Members.Select(e => e.Item1).
                Where(m => m.Status == SiloStatus.Active && m.ProxyPort != 0).
                Select(m =>
                {
                    var gatewayAddress = SiloAddress.New(m.SiloAddress.Endpoint.Address, m.ProxyPort, m.SiloAddress.Generation);
                    return gatewayAddress.ToGatewayUri();
                }).ToList();
        }

        /// <summary>
        /// 指定该 IGatewayListProvider 刷新的次数,以锁定其返回信息的最大淡度。
        /// </summary>
        public TimeSpan MaxStaleness => _maxStaleness;

        /// <summary>
        /// 指定此 IGatewayListProvider 是否刷新其返回的信息,还是总是返回相同的 gw 列表。
        /// (目前只有基于静态配置 StaticGatewayListProvider 的配置不可上调。所有其他都是 。)
        /// </summary>
        public bool IsUpdatable => true;
    }
}
