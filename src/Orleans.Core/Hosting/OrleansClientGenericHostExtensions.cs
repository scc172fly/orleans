using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// <see cref="IHostBuilder"/>的推广方法。
    /// </summary>
    public static class OrleansClientGenericHostExtensions
    {
        private static readonly Type MarkerType = typeof(OrleansBuilderMarker);

        /// <summary>
        /// 配置主机应用程序构建器以托管 Orleans 客户端 。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 请注意,这一方法不应与 HostApplicationBuilder.UseOrleans结合使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 无效。</exception>
        public static HostApplicationBuilder UseOrleansClient(this HostApplicationBuilder hostAppBuilder)
            => UseOrleansClient(hostAppBuilder, _ => { });

        /// <summary>
        /// 配置主机应用程序构建器以托管 Orleans 客户端 。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 HostApplicationBuilder.UseOrleans结合使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static HostApplicationBuilder UseOrleansClient(
            this HostApplicationBuilder hostAppBuilder,
            Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            hostAppBuilder.Services.AddOrleansClient(hostAppBuilder.Configuration, configureDelegate);

            return hostAppBuilder;
        }

        /// 配置主机应用程序构建器以托管 Orleans 客户端 。
        /// <summary>
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 请注意,这一方法不应与 IHostApplicationBuilder.UseOrleans结合使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 无效。</exception>
        public static IHostApplicationBuilder UseOrleansClient(this IHostApplicationBuilder hostAppBuilder)
            => UseOrleansClient(hostAppBuilder, _ => { });

        /// <summary>
        /// 配置主机应用程序构建器以托管 Orleans 客户端 。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 IHostApplicationBuilder.UseOrleans结合使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static IHostApplicationBuilder UseOrleansClient(
            this IHostApplicationBuilder hostAppBuilder,
            Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            hostAppBuilder.Services.AddOrleansClient(hostAppBuilder.Configuration, configureDelegate);

            return hostAppBuilder;
        }

        /// <summary>
        /// 配置主机构建器以主机 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 请注意,这一方法不应与 IHostBuilder.UseOrleans一起使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 无效。</exception>
        public static IHostBuilder UseOrleansClient(this IHostBuilder hostBuilder) => hostBuilder.UseOrleansClient((_, _) => { });

        /// <summary>
        /// 配置主机构建器以主机 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 IHostBuilder.UseOrleans一起使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static IHostBuilder UseOrleansClient(this IHostBuilder hostBuilder, Action<IClientBuilder> configureDelegate) =>
            hostBuilder.UseOrleansClient((_, clientBuilder) => configureDelegate(clientBuilder));

        /// <summary>
        /// 配置主机构建器以主机 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 IHostBuilder.UseOrleans一起使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static IHostBuilder UseOrleansClient(this IHostBuilder hostBuilder, Action<HostBuilderContext, IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            if (hostBuilder.Properties.ContainsKey("HasOrleansSiloBuilder"))
            {
                throw GetOrleansSiloAddedException();
            }

            hostBuilder.Properties["HasOrleansClientBuilder"] = "true";

            return hostBuilder.ConfigureServices((ctx, services) => configureDelegate(ctx, AddOrleansClient(services, ctx.Configuration)));
        }

        /// <summary>
        /// 配置服务收藏以主机 Orleans 客户端。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>服务收藏。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 UseOrleans一起使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static IServiceCollection AddOrleansClient(this IServiceCollection services, Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var clientBuilder = AddOrleansClient(services, configuration: null);
            configureDelegate(clientBuilder);
            return services;
        }

        /// <summary>
        /// 配置服务收藏以主机 Orleans 客户端。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="configureDelegate">用于配置客户端的代表。</param>
        /// <returns>服务收藏。</returns>
        /// <remarks>
        /// 在同一 <see cref="IClientBuilder"/> 实例中多次调用此方法, 将配置一个客户端 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// 请注意,这一方法不应与 UseOrleans一起使用,因为 UseOrleans 自动包括客户。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 无效或 <paramref name="configureDelegate"/> 无效。</exception>
        public static IServiceCollection AddOrleansClient(this IServiceCollection services, IConfiguration? configuration, Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var clientBuilder = AddOrleansClient(services, configuration: configuration);
            configureDelegate(clientBuilder);
            return services;
        }

        private static IClientBuilder AddOrleansClient(IServiceCollection services, IConfiguration? configuration)
        {
            configuration ??= new ConfigurationBuilder().Build();
            IClientBuilder? clientBuilder = default;
            foreach (var descriptor in services.Where(d => d.ServiceType.Equals(MarkerType)))
            {
                var instance = (OrleansBuilderMarker)descriptor.ImplementationInstance!;
                clientBuilder = instance.BuilderInstance switch
                {
                    IClientBuilder existingBuilder => existingBuilder,
                    _ => throw GetOrleansSiloAddedException()
                };
            }

            if (clientBuilder is null)
            {
                clientBuilder = new ClientBuilder(services, configuration);
                services.AddSingleton(new OrleansBuilderMarker(clientBuilder));
            }

            return clientBuilder;
        }

        private static OrleansConfigurationException GetOrleansSiloAddedException() =>
            new("Do not use UseOrleans with UseOrleansClient. If you want a client and server in the same process, only UseOrleans is necessary and the UseOrleansClient call can be removed.");
    }

    /// <summary>
    /// 用于在服务收藏中存储构建器的标记类型 。
    /// </summary>
    internal sealed class OrleansBuilderMarker
    {
        /// <summary>
        /// 初始化 <see cref="OrleansBuilderMarker"/> 类的新实例。
        /// </summary>
        /// <param name="builderInstance">构建方实例。</param>
        public OrleansBuilderMarker(object builderInstance) => BuilderInstance = builderInstance;

        /// <summary>
        /// 获得建筑商的例子。
        /// </summary>
        public object BuilderInstance { get; }
    }
}
