using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.BroadcastChannel;
using Orleans.BroadcastChannel.SubscriberTable;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置和解析广播通道提供程序的扩展方法。
    /// </summary>
    public static class ChannelHostingExtensions
    {
        /// <summary>
        /// 向 Silo 添加新的广播通道。
        /// </summary>
        /// <param name="this">构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
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
        /// 向 Silo 添加新的广播通道。
        /// </summary>
        /// <param name="this">构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddBroadcastChannel(this ISiloBuilder @this, string name, Action<OptionsBuilder<BroadcastChannelOptions>>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);

            @this.Services.AddBroadcastChannel(name, configureOptions);
            @this.AddGrainExtension<IBroadcastChannelConsumerExtension, BroadcastChannelConsumerExtension>();
            return @this;
        }

        /// <summary>
        /// 向客户端添加新的广播通道。
        /// </summary>
        /// <param name="this">构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">配置委托。</param>
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
        /// 向客户端添加新的广播通道。
        /// </summary>
        /// <param name="this">构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddBroadcastChannel(this IClientBuilder @this, string name, Action<OptionsBuilder<BroadcastChannelOptions>>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(@this, nameof(@this));
            ArgumentNullException.ThrowIfNull(name);

            @this.Services.AddBroadcastChannel(name, configureOptions);
            return @this;
        }

        /// <summary>
        /// 获取命名广播通道提供程序。
        /// </summary>
        /// <param name="this">客户端。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>命名广播通道提供程序。</returns>
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
