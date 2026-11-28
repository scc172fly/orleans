using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// <see cref="IHostBuilder"/> 的扩展方法。
    /// </summary>
    public static class OrleansClientGenericHostExtensions
    {
        private static readonly Type MarkerType = typeof(OrleansBuilderMarker);

        /// <summary>
        /// 将宿主应用程序构建器配置为承载 Orleans 客户端。
        /// </summary>
        /// <param name="hostAppBuilder">宿主应用程序构建器。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 注意，不应将此方法与 HostApplicationBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 为 null。</exception>
        public static HostApplicationBuilder UseOrleansClient(this HostApplicationBuilder hostAppBuilder)
            => UseOrleansClient(hostAppBuilder, _ => { });

        /// <summary>
        /// 将宿主应用程序构建器配置为承载 Orleans 客户端。
        /// </summary>
        /// <param name="hostAppBuilder">宿主应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 HostApplicationBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
        public static HostApplicationBuilder UseOrleansClient(
            this HostApplicationBuilder hostAppBuilder,
            Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            hostAppBuilder.Services.AddOrleansClient(hostAppBuilder.Configuration, configureDelegate);

            return hostAppBuilder;
        }

        /// 将宿主应用程序构建器配置为承载 Orleans 客户端。
        /// <summary>
        /// </summary>
        /// <param name="hostAppBuilder">宿主应用程序构建器。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 注意，不应将此方法与 IHostApplicationBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 为 null。</exception>
        public static IHostApplicationBuilder UseOrleansClient(this IHostApplicationBuilder hostAppBuilder)
            => UseOrleansClient(hostAppBuilder, _ => { });

        /// <summary>
        /// 将宿主应用程序构建器配置为承载 Orleans 客户端。
        /// </summary>
        /// <param name="hostAppBuilder">宿主应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 IHostApplicationBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostAppBuilder"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
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
        /// 配置宿主生成器以托管 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 注意，不应将此方法与 IHostBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 为 null。</exception>
        public static IHostBuilder UseOrleansClient(this IHostBuilder hostBuilder) => hostBuilder.UseOrleansClient((_, _) => { });

        /// <summary>
        /// 配置宿主生成器以托管 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 IHostBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
        public static IHostBuilder UseOrleansClient(this IHostBuilder hostBuilder, Action<IClientBuilder> configureDelegate) =>
            hostBuilder.UseOrleansClient((_, clientBuilder) => configureDelegate(clientBuilder));

        /// <summary>
        /// 配置宿主生成器以托管 Orleans 客户端。
        /// </summary>
        /// <param name="hostBuilder">宿主构建器。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>宿主构建器。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 IHostBuilder.UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="hostBuilder"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
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
        /// 配置服务集合以托管 Orleans 客户端。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>服务集合。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
        public static IServiceCollection AddOrleansClient(this IServiceCollection services, Action<IClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var clientBuilder = AddOrleansClient(services, configuration: null);
            configureDelegate(clientBuilder);
            return services;
        }

        /// <summary>
        /// 配置服务集合以托管 Orleans 客户端。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="configureDelegate">用于配置客户端的委托。</param>
        /// <returns>服务集合。</returns>
        /// <remarks>
        /// 对同一个 <see cref="IClientBuilder"/> 实例多次调用此方法，最终只会配置一个客户端。
        /// 不过，<paramref name="configureDelegate"/> 的效果会在每次调用时应用一次。
        /// 注意，不应将此方法与 UseOrleans 一起使用，因为 UseOrleans 会自动包含客户端。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null，或 <paramref name="configureDelegate"/> 为 null。</exception>
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
    /// 用于将生成器存入服务集合的标记类型。
    /// </summary>
    internal sealed class OrleansBuilderMarker
    {
        /// <summary>
        /// 初始化 <see cref="OrleansBuilderMarker"/> 类的新实例。
        /// </summary>
        /// <param name="builderInstance">生成器实例。</param>
        public OrleansBuilderMarker(object builderInstance) => BuilderInstance = builderInstance;

        /// <summary>
        /// 获取生成器实例。
        /// </summary>
        public object BuilderInstance { get; }
    }
}
