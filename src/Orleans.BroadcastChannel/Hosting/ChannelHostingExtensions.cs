using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.BroadcastChannel;
using Orleans.BroadcastChannel.SubscriberTable;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供扩大方法,用于配置和解决广播频道供应商。
    /// </summary>
    public static class ChannelHostingExtensions
    {
        /// <summary>
        /// 在 silo中添加一个新的广播频道。
        /// </summary>
        /// <param name="this">建筑工。</param>
        /// <param name="name">提供者的名称</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddBroadcastChannel(this ISiloBuilder @this, string name, Action<BroadcastChannelOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureOptions);

            @this.Services.AddBroadcastChannel(name, ob => ob.Configure(configureOptions));
            @this.AddGrainExtension<IBroadcastChannelConsumerExtension, BroadcastChannelConsumerExtension>();
            return @this;
        }

        /// <summary>
        /// 在 silo中添加一个新的广播频道。
        /// </summary>
        /// <param name="this">建筑工。</param>
        /// <param name="name">提供者的名称</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddBroadcastChannel(this ISiloBuilder @this, string name, Action<OptionsBuilder<BroadcastChannelOptions>>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);

            @this.Services.AddBroadcastChannel(name, configureOptions);
            @this.AddGrainExtension<IBroadcastChannelConsumerExtension, BroadcastChannelConsumerExtension>();
            return @this;
        }

        /// <summary>
        /// 向客户添加一个新的广播频道。
        /// </summary>
        /// <param name="this">建筑工。</param>
        /// <param name="name">提供者的名称</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddBroadcastChannel(this IClientBuilder @this, string name, Action<BroadcastChannelOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureOptions);

            @this.Services.AddBroadcastChannel(name, ob => ob.Configure(configureOptions));
            return @this;
        }

        /// <summary>
        /// 向客户添加一个新的广播频道。
        /// </summary>
        /// <param name="this">建筑工。</param>
        /// <param name="name">提供者的名称</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddBroadcastChannel(this IClientBuilder @this, string name, Action<OptionsBuilder<BroadcastChannelOptions>>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);

            @this.Services.AddBroadcastChannel(name, configureOptions);
            return @this;
        }

        /// <summary>
        /// 得到了一个叫广播频道供应商。
        /// </summary>
        /// <param name="this">客户。</param>
        /// <param name="name">提供者的名称</param>
        /// <returns>名为广播频道供应商。</returns>
        public static IBroadcastChannelProvider GetBroadcastChannelProvider(this IClusterClient @this, string name)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);

            return @this.ServiceProvider.GetRequiredKeyedService<IBroadcastChannelProvider>(name);
        }

        private static void AddBroadcastChannel(this IServiceCollection services, string name, Action<OptionsBuilder<BroadcastChannelOptions>>? configureOptions)
        {
            configureOptions?.Invoke(services.AddOptions<BroadcastChannelOptions>(name));
            services.ConfigureNamedOptionForLogging<BroadcastChannelOptions>(name);
            services
                .AddSingleton<ImplicitChannelSubscriberTable>()
                .AddSingleton<IChannelNamespacePredicateProvider, DefaultChannelNamespacePredicateProvider>()
                .AddSingleton<IChannelNamespacePredicateProvider, ConstructorChannelNamespacePredicateProvider>()
                .AddKeyedSingleton<IChannelIdMapper, DefaultChannelIdMapper>(DefaultChannelIdMapper.Name)
                .AddKeyedSingleton(name, (sp, key) => BroadcastChannelProvider.Create(sp, (key as string)!));
        }
    }
}
