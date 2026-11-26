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
    /// 向制作人提供广播频道的功能。
    /// </summary>
    public interface IBroadcastChannelProvider
    {
        /// <summary>
        /// 找一个作家去频道
        /// </summary>
        /// <typeparam name="T">频道元素类型。</typeparam>
        /// <param name="streamId">频道标识符。</param>
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
        /// 创建新频道提供者 。
        /// </summary>
        /// <param name="sp">服务供应商。</param>
        /// <param name="name">提供者的名称。</param>
        /// <returns>指定的频道提供者。</returns>
        public static IBroadcastChannelProvider Create(IServiceProvider sp, string name)
        {
            var opt = sp.GetOptionsByName<BroadcastChannelOptions>(name);
            return ActivatorUtilities.CreateInstance<BroadcastChannelProvider>(sp, name, opt);
        }
    }
}
