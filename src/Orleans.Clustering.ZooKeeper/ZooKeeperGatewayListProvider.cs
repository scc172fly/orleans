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
    /// 从 ZooKeeper 集群成员资格信息中提供 Orleans 网关地址。
    /// </summary>
    public class ZooKeeperGatewayListProvider : IGatewayListProvider
    {
        private readonly ZooKeeperWatcher _watcher;

        /// <summary>
        /// 此部署的节点名称，例如 /ClusterId。
        /// </summary>
        private readonly string _deploymentPath;

        /// <summary>
        /// 部署连接字符串，例如 "192.168.1.1,192.168.1.2/ClusterId"。
        /// </summary>
        private readonly string _deploymentConnectionString;
        private readonly TimeSpan _maxStaleness;
        private readonly Func<ZooKeeperSession> _createSession;
        private readonly ResiliencePipeline _readRetryPipeline;

        /// <summary>
        /// 初始化 <see cref="ZooKeeperGatewayListProvider"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="options">ZooKeeper 网关发现选项。</param>
        /// <param name="gatewayOptions">网关发现刷新选项。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="logger"/>、<paramref name="options"/>、<paramref name="gatewayOptions"/> 或
        /// <paramref name="clusterOptions"/> 为 <see langword="null"/>。
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
        /// 初始化基于 ZooKeeper 的网关提供程序。
        /// </summary>
        public Task InitializeGatewayListProvider() => InitializeGatewayListProviderAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 返回客户端可用于连接 Orleans 集群的网关（Silo）列表。
        /// URI 格式为 "gwy.tcp://IP:port/Generation"。有关格式详情，请参见 Utils.ToGatewayUri 和 Utils.ToSiloAddress。
        /// </summary>
        /// <remarks>
        /// 网关发现使用受版本屏障保护的成员资格快照。原生读取在连接丢失时
        /// 最多在当前操作的会话上重试四次，然后传递最终故障。
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
        /// 指定刷新此 IGatewayListProvider 的频率，以限制其返回信息的最长过期时间。
        /// </summary>
        public TimeSpan MaxStaleness => _maxStaleness;

        /// <summary>
        /// 指定此 IGatewayListProvider 是否刷新返回的信息，还是始终返回相同的网关列表。
        /// （目前仅基于静态配置的 StaticGatewayListProvider 不可更新，其他提供程序均可更新。）
        /// </summary>
        public bool IsUpdatable => true;
    }
}
