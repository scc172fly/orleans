using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IClientBuilder"/>的推广方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置所提供代表作为连接再试过滤器的配置,用于确定是否在失败后重新访问与 Orleans 组群的初始连接。
        /// </summary>
        /// <param name="builder">主机建造者。</param>
        /// <param name="connectionRetryFilter">连接重试过滤器。</param>
        /// <returns>同一案件 <see cref="IClientBuilder"/> 系链。</returns>
        public static IClientBuilder UseConnectionRetryFilter(this IClientBuilder builder, Func<Exception, CancellationToken, Task<bool>> connectionRetryFilter)
        {
            return builder.ConfigureServices(collection => collection.AddSingleton<IClientConnectionRetryFilter>(new DelegateConnectionRetryFilter(connectionRetryFilter)));
        }

        /// <summary>
        /// 配置所提供代表作为连接再试过滤器的配置,用于确定是否在失败后重新访问与 Orleans 组群的初始连接。
        /// </summary>
        /// <param name="builder">主机建造者。</param>
        /// <param name="connectionRetryFilter">连接重试过滤器。</param>
        /// <returns>同一案件 <see cref="IClientBuilder"/> 系链。</returns>
        public static IClientBuilder UseConnectionRetryFilter(this IClientBuilder builder, IClientConnectionRetryFilter connectionRetryFilter)
        {
            return builder.ConfigureServices(collection => collection.AddSingleton<IClientConnectionRetryFilter>(connectionRetryFilter));
        }

        /// <summary>
        /// 配置提供的 <typeparamref name="TConnectionRetryFilter"/> 类型,作为连接再试过滤器,用于确定是否在失败后重新尝试与 Orleans 组的初始连接。
        /// </summary>
        /// <param name="builder">主机建造者。</param>
        /// <returns>同一案件 <see cref="IClientBuilder"/> 系链。</returns>
        public static IClientBuilder UseConnectionRetryFilter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConnectionRetryFilter>(
            this IClientBuilder builder)
            where TConnectionRetryFilter : class, IClientConnectionRetryFilter
        {
            return builder.ConfigureServices(collection => collection.AddSingleton<IClientConnectionRetryFilter, TConnectionRetryFilter>());
        }

        private sealed class DelegateConnectionRetryFilter : IClientConnectionRetryFilter
        {
            private readonly Func<Exception, CancellationToken, Task<bool>> _filter;
            public DelegateConnectionRetryFilter(Func<Exception, CancellationToken, Task<bool>> connectionRetryFilter) => _filter = connectionRetryFilter ?? throw new ArgumentNullException(nameof(connectionRetryFilter));
            public Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken) => _filter(exception, cancellationToken);
        }

        /// <summary>
        /// 添加容器服务。 这可以被多次调用, 结果将是添加 。
        /// </summary>
        /// <param name="builder">主机建造者。</param>
        /// <param name="configureDelegate"></param>
        /// <returns>同一案件 <see cref="IClientBuilder"/> 系链。</returns>
        public static IClientBuilder ConfigureServices(this IClientBuilder builder, Action<IServiceCollection> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (configureDelegate == null) throw new ArgumentNullException(nameof(configureDelegate));
            configureDelegate(builder.Services);
            return builder;
        }

        /// <summary>
        /// 注册用于配置特定选项类型的动作。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">主机建造者。</param>
        /// <param name="configureOptions">用于配置选项的动作。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder Configure<TOptions>(this IClientBuilder builder, Action<TOptions> configureOptions) where TOptions : class
        {
            return builder.ConfigureServices(services => services.Configure(configureOptions));
        }

        /// <summary>
        /// 登记一个 <typeparamref name="TOptions"/> 将约束的配置实例。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">主机建造者。</param>
        /// <param name="configuration">配置。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder Configure<TOptions>(this IClientBuilder builder, IConfiguration configuration) where TOptions : class
        {
            return builder.ConfigureServices(services => services.AddOptions<TOptions>().Bind(configuration));
        }

        /// <summary>
        /// 注册一个 <see cref="GatewayCountChangedHandler"/> 事件处理程序 。
        /// </summary>
        public static IClientBuilder AddGatewayCountChangedHandler(this IClientBuilder builder, GatewayCountChangedHandler handler)
        {
            builder.ConfigureServices(services => services.AddSingleton(handler));
            return builder;
        }

        /// <summary>
        /// 注册一个 <see cref="GatewayCountChangedHandler"/> 事件处理程序 。
        /// </summary>
        public static IClientBuilder AddGatewayCountChangedHandler(this IClientBuilder builder, Func<IServiceProvider, GatewayCountChangedHandler> handlerFactory)
        {
            builder.ConfigureServices(services => services.AddSingleton(handlerFactory));
            return builder;
        }

        /// <summary>
        /// 登记一个与集群连接的观察员地位观察员。
        /// </summary>
        public static IClientBuilder AddClusterConnectionStatusObserver<TObserver>(this IClientBuilder builder, TObserver observer)
            where TObserver : IClusterConnectionStatusObserver
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(observer);
            builder.Services.AddSingleton<IClusterConnectionStatusObserver>(observer);
            return builder;
        }

        /// <summary>
        /// 登记一个与集群连接的观察员地位观察员。
        /// </summary>
        public static IClientBuilder AddClusterConnectionStatusObserver<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TObserver>(
            this IClientBuilder builder)
            where TObserver : class, IClusterConnectionStatusObserver
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddSingleton<IClusterConnectionStatusObserver, TObserver>();
            return builder;
        }

        /// <summary>
        /// 注册一个 <see cref="ConnectionToClusterLostHandler"/> 事件处理程序 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="handler">处理器。</param>
        /// <returns>建筑工。</returns>
        public static IClientBuilder AddClusterConnectionLostHandler(this IClientBuilder builder, ConnectionToClusterLostHandler handler)
        {
            builder.ConfigureServices(services => services.AddSingleton(handler));
            return builder;
        }

        /// <summary>
        /// 注册一个 <see cref="ConnectionToClusterLostHandler"/> 事件处理程序 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="handlerFactory">处理厂</param>
        /// <returns>建筑工。</returns>
        public static IClientBuilder AddClusterConnectionLostHandler(this IClientBuilder builder, Func<IServiceProvider, ConnectionToClusterLostHandler> handlerFactory)
        {
            builder.ConfigureServices(services => services.AddSingleton(handlerFactory));
            return builder;
        }

        /// <summary>
        /// 添加添加添加 <see cref="Activity.Current"/> 通过 grain 电话
        /// 注:根据 <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> 活动,只有在存在任何活动听众时才会创建 <see cref="ActivitySource.HasListeners()"/> 和 <see cref="ActivityListener.Sample"/> 返回 <see cref="ActivitySamplingResult.PropagationData"/>。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
        public static IClientBuilder AddActivityPropagation(this IClientBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton(DistributedContextPropagator.Current);

            return builder
                .AddOutgoingGrainCallFilter<ActivityPropagationOutgoingGrainCallFilter>()
                .AddIncomingGrainCallFilter<ActivityPropagationIncomingGrainCallFilter>();
        }

        /// <summary>
        /// 配置客户端连接到本地主机上的 silo 。
        /// </summary>
        /// <param name="builder">
        /// 客户的建设者
        /// </param>
        /// <param name="gatewayPort">
        /// 本地 silo的网关端口。
        /// </param>
        /// <param name="serviceId">
        /// 服务代号
        /// </param>
        /// <param name="clusterId">
        /// 组号 ID 。
        /// </param>
        /// <returns>
        /// 下 <see cref="IClientBuilder"/>.
        /// </returns>
        public static IClientBuilder UseLocalhostClustering(
            this IClientBuilder builder,
            int gatewayPort = 30000,
            string serviceId = ClusterOptions.DevelopmentServiceId,
            string clusterId = ClusterOptions.DevelopmentClusterId)
        {
            return builder.UseLocalhostClustering(new[] { gatewayPort }, serviceId, clusterId);
        }

        /// <summary>
        /// 配置客户端连接到本地主机上的 silo 。
        /// </summary>
        /// <param name="builder">
        /// 客户的建设者
        /// </param>
        /// <param name="gatewayPorts">
        /// 本地 silo 网关端口。
        /// </param>
        /// <param name="serviceId">
        /// 服务代号
        /// </param>
        /// <param name="clusterId">
        /// 组号 ID 。
        /// </param>
        /// <returns>
        /// 下 <see cref="IClientBuilder"/>.
        /// </returns>
        public static IClientBuilder UseLocalhostClustering(this IClientBuilder builder,
            int[] gatewayPorts,
            string serviceId = ClusterOptions.DevelopmentServiceId,
            string clusterId = ClusterOptions.DevelopmentClusterId)
        {
            return builder.UseStaticClustering(gatewayPorts.Select(p => new IPEndPoint(IPAddress.Loopback, p)).ToArray())
                .ConfigureServices(services =>
                {
                    // 如果调用者没有覆盖服务 ID 或群集 id, 请将默认值配置为后退 。
                    if (string.Equals(serviceId, ClusterOptions.DevelopmentServiceId, StringComparison.Ordinal) && string.Equals(clusterId, ClusterOptions.DevelopmentClusterId, StringComparison.Ordinal))
                    {
                        services.PostConfigure<ClusterOptions>(options =>
                        {
                            if (string.IsNullOrWhiteSpace(options.ClusterId)) options.ClusterId = ClusterOptions.DevelopmentClusterId;
                            if (string.IsNullOrWhiteSpace(options.ServiceId)) options.ServiceId = ClusterOptions.DevelopmentServiceId;
                        });
                    }
                    else
                    {
                        services.Configure<ClusterOptions>(options =>
                        {
                            options.ServiceId = serviceId;
                            options.ClusterId = clusterId;
                        });
                    }
                });
        }

        /// <summary>
        /// 配置客户端使用静态组合。
        /// </summary>
        /// <param name="builder">
        /// 客户的建设者
        /// </param>
        /// <param name="endpoints">
        /// 网关终点
        /// </param>
        /// <returns>
        /// 下 <see cref="IClientBuilder"/>.
        /// </returns>
        public static IClientBuilder UseStaticClustering(this IClientBuilder builder, params IPEndPoint[] endpoints)
        {
            return builder.UseStaticClustering(options => options.Gateways = endpoints.Select(ep => ep.ToGatewayUri()).ToList());
        }

        /// <summary>
        /// 配置客户端使用静态组合。
        /// </summary>
        /// <param name="builder">
        /// 客户的建设者
        /// </param>
        /// <param name="configureOptions">
        /// 配置选项 。
        /// </param>
        /// <returns>
        /// 下 <see cref="IClientBuilder"/>.
        /// </returns>
        public static IClientBuilder UseStaticClustering(this IClientBuilder builder, Action<StaticGatewayListProviderOptions> configureOptions)
        {
            return builder.ConfigureServices(
                collection =>
                {
                    if (configureOptions != null)
                    {
                        collection.Configure(configureOptions);
                    }

                    collection.AddSingleton<IGatewayListProvider, StaticGatewayListProvider>()
                        .ConfigureFormatter<StaticGatewayListProviderOptions>();
                });
        }

        /// <summary>
        /// 配置客户端使用静态组合。
        /// </summary>
        /// <param name="builder">
        /// 客户的建设者
        /// </param>
        /// <param name="configureOptions">
        /// 配置选项 。
        /// </param>
        /// <returns>
        /// 下 <see cref="IClientBuilder"/>.
        /// </returns>
        public static IClientBuilder UseStaticClustering(this IClientBuilder builder, Action<OptionsBuilder<StaticGatewayListProviderOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                collection =>
                {
                    configureOptions?.Invoke(collection.AddOptions<StaticGatewayListProviderOptions>());
                    collection.AddSingleton<IGatewayListProvider, StaticGatewayListProvider>()
                        .ConfigureFormatter<StaticGatewayListProviderOptions>();
                });
        }
    }
}
