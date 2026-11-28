using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.BroadcastChannel.SubscriberTable;
using Orleans.Configuration;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 为生产者提供广播通道的功能。
    /// </summary>
    public interface IBroadcastChannelProvider
    {
        /// <summary>
        /// 获取通道的写入器。
        /// </summary>
        /// <typeparam name="T">通道元素类型。</typeparam>
        /// <param name="streamId">通道标识符。</param>
        /// <returns></returns>
        IBroadcastChannelWriter<T> GetChannelWriter<T>(ChannelId streamId);
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Create instantiates this type through ActivatorUtilities for the keyed IBroadcastChannelProvider registration.")]
    internal class BroadcastChannelProvider : IBroadcastChannelProvider
    {
        private readonly string _providerName;
        private readonly BroadcastChannelOptions _options;
        private readonly IGrainFactory _grainFactory;
        private readonly ImplicitChannelSubscriberTable _subscriberTable;
        private readonly ILoggerFactory _loggerFactory;
        private readonly SiloAddress? _siloAddress;
        private readonly string _clusterId;

        public BroadcastChannelProvider(
            string providerName,
            BroadcastChannelOptions options,
            IGrainFactory grainFactory,
            ImplicitChannelSubscriberTable subscriberTable,
            ILoggerFactory loggerFactory,
            IServiceProvider serviceProvider)
        {
            _providerName = providerName;
            _options = options;
            _grainFactory = grainFactory;
            _subscriberTable = subscriberTable;
            _loggerFactory = loggerFactory;
            _siloAddress = serviceProvider.GetService<ILocalSiloDetails>()?.SiloAddress;
            _clusterId = serviceProvider.GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId;
        }

        /// <inheritdoc />
        public IBroadcastChannelWriter<T> GetChannelWriter<T>(ChannelId streamId)
        {
            return new BroadcastChannelWriter<T>(
                new InternalChannelId(_providerName, streamId),
                _grainFactory,
                _subscriberTable,
                _options.FireAndForgetDelivery,
                _loggerFactory,
                _siloAddress,
                _clusterId);
        }

        /// <summary>
        /// 创建新的通道提供程序。
        /// </summary>
        /// <param name="sp">服务提供程序。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>命名通道提供程序。</returns>
        public static IBroadcastChannelProvider Create(IServiceProvider sp, string name)
        {
            var opt = sp.GetOptionsByName<BroadcastChannelOptions>(name);
            return ActivatorUtilities.CreateInstance<BroadcastChannelProvider>(sp, name, opt);
        }
    }
}
