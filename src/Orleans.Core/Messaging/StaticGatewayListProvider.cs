using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Messaging
{
    /// <summary>
    /// <see cref="IGatewayListProvider"/> 执行返回通过 <see cref="StaticGatewayListProviderOptions"/>配置的静态列表。
    /// </summary>
    public class StaticGatewayListProvider : IGatewayListProvider
    {
        private readonly StaticGatewayListProviderOptions options;
        private readonly TimeSpan maxStaleness;

        /// <summary>
        /// 初始化 <see cref="StaticGatewayListProvider"/> 类的新实例。
        /// </summary>
        /// <param name="options">具体备选办法。</param>
        /// <param name="gatewayOptions">通用网关选项。</param>
        public StaticGatewayListProvider(IOptions<StaticGatewayListProviderOptions> options, IOptions<GatewayOptions> gatewayOptions)
        {
            this.options = options.Value;
            this.maxStaleness = gatewayOptions.Value.GatewayListRefreshPeriod;
        }

        /// <inheritdoc />
        public Task InitializeGatewayListProvider() => InitializeGatewayListProviderAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task InitializeGatewayListProviderAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IList<Uri>> GetGateways() => GetGatewaysAsync(CancellationToken.None);

        /// <inheritdoc />
        public Task<IList<Uri>> GetGatewaysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IList<Uri>>(this.options.Gateways);
        }

        /// <inheritdoc />
        public TimeSpan MaxStaleness
        {
            get => this.maxStaleness;
        }

        /// <inheritdoc />
        public bool IsUpdatable
        {
            get => true;
        }
    }
}
