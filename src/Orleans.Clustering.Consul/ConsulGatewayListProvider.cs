using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Orleans.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime.Membership
{
    /// <summary>
    /// 从 Consul 集群成员资格信息中提供 Orleans 网关地址。
    /// </summary>
    public class ConsulGatewayListProvider : IGatewayListProvider
    {
        private IConsulClient consulClient = null!;
        private readonly string clusterId;
        private readonly ILogger logger;
        private readonly ConsulClusteringOptions options;
        private readonly TimeSpan maxStaleness;
        private readonly string? kvRootFolder;

        /// <summary>
        /// 初始化 <see cref="ConsulGatewayListProvider"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="options">Consul 集群成员管理选项。</param>
        /// <param name="gatewayOptions">网关发现选项。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        public ConsulGatewayListProvider(
            ILogger<ConsulGatewayListProvider> logger,
            IOptions<ConsulClusteringOptions> options,
            IOptions<GatewayOptions> gatewayOptions,
            IOptions<ClusterOptions> clusterOptions)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(gatewayOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);

            this.logger = logger;
            this.clusterId = clusterOptions.Value.ClusterId;
            this.maxStaleness = gatewayOptions.Value.GatewayListRefreshPeriod;
            this.options = options.Value;
            this.kvRootFolder = options.Value.KvRootFolder;
        }

        /// <inheritdoc />
        public TimeSpan MaxStaleness
        {
            get { return this.maxStaleness; }
        }

        /// <inheritdoc />
        public bool IsUpdatable
        {
            get { return true; }
        }
        /// <inheritdoc />
        public Task InitializeGatewayListProvider() => InitializeGatewayListProviderAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            consulClient = options.CreateClient();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IList<Uri>> GetGateways() => GetGatewaysAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var membershipTableData = await ConsulBasedMembershipTable.ReadAllAsync(this.consulClient, this.clusterId, this.kvRootFolder, this.logger, null, cancellationToken);
            if (membershipTableData == null) return new List<Uri>();

            return membershipTableData.Members.Select(e => e.Item1).
                Where(m => m.Status == SiloStatus.Active && m.ProxyPort != 0).
                Select(m =>
                {
                    var gatewayAddress = SiloAddress.New(m.SiloAddress.Endpoint.Address, m.ProxyPort, m.SiloAddress.Generation);
                    return gatewayAddress.ToGatewayUri();
                }).ToList();
        }
    }


}
