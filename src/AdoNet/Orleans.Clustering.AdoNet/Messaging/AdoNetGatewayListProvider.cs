using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Clustering.AdoNet.Storage;
using Orleans.Messaging;
using Orleans.Configuration;

namespace Orleans.Runtime.Membership
{
    /// <summary>
    /// 从关系数据库集群成员表提供 Orleans 网关地址。
    /// </summary>
    public partial class AdoNetGatewayListProvider : IGatewayListProvider
    {
        private readonly ILogger _logger;
        private readonly string _clusterId;
        private readonly AdoNetClusteringClientOptions _options;
        private RelationalOrleansQueries _orleansQueries = null!;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _maxStaleness;

        /// <summary>
        /// 初始化 <see cref="AdoNetGatewayListProvider"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="options">关系数据库集群成员管理选项。</param>
        /// <param name="gatewayOptions">网关发现选项。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        /// <exception cref="ArgumentNullException">必需的选项访问器为 <see langword="null"/>。</exception>
        public AdoNetGatewayListProvider(
            ILogger<AdoNetGatewayListProvider> logger,
            IServiceProvider serviceProvider,
            IOptions<AdoNetClusteringClientOptions> options,
            IOptions<GatewayOptions> gatewayOptions,
            IOptions<ClusterOptions> clusterOptions)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(gatewayOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);

            this._logger = logger;
            this._serviceProvider = serviceProvider;
            this._options = options.Value;
            this._clusterId = clusterOptions.Value.ClusterId;
            this._maxStaleness = gatewayOptions.Value.GatewayListRefreshPeriod;
        }

        /// <inheritdoc />
        public TimeSpan MaxStaleness
        {
            get { return this._maxStaleness; }
        }

        /// <inheritdoc />
        public bool IsUpdatable
        {
            get { return true; }
        }

        /// <inheritdoc />
        public Task InitializeGatewayListProvider() => InitializeGatewayListProviderAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceInitializeGatewayListProvider();
            _orleansQueries = await RelationalOrleansQueries.CreateInstance(_options.Invariant, _options.ConnectionString, _options.DataSource, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IList<Uri>> GetGateways() => GetGatewaysAsync(CancellationToken.None);

        /// <inheritdoc />
        public async Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceGetGateways();
            try
            {
                return await _orleansQueries.ActiveGatewaysAsync(this._clusterId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDebugGatewaysFailed(ex);
                throw;
            }
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetGatewayListProvider)}.{nameof(InitializeGatewayListProvider)} called."
        )]
        private partial void LogTraceInitializeGatewayListProvider();

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = $"{nameof(AdoNetGatewayListProvider)}.{nameof(GetGateways)} called."
        )]
        private partial void LogTraceGetGateways();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AdoNetGatewayListProvider)}.{nameof(GetGateways)} failed"
        )]
        private partial void LogDebugGatewaysFailed(Exception exception);
    }
}
