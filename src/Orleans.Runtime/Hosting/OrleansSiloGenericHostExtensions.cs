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
    public static class OrleansSiloGenericHostExtensions
    {
        private static readonly Type MarkerType = typeof(OrleansBuilderMarker);

        /// <summary>
        /// 配置主机应用程序构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IHostApplicationBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// </remarks>
        public static IHostApplicationBuilder UseOrleans(
            this IHostApplicationBuilder hostAppBuilder)
            => hostAppBuilder.UseOrleans(_ => { });

        /// <summary>
        /// 配置主机应用程序构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="HostApplicationBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// </remarks>
        public static HostApplicationBuilder UseOrleans(
            this HostApplicationBuilder hostAppBuilder)
            => hostAppBuilder.UseOrleans(_ => { });

        /// <summary>
        /// 配置主机构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置 silo的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IHostApplicationBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// </remarks>
        public static IHostApplicationBuilder UseOrleans(
            this IHostApplicationBuilder hostAppBuilder,
            Action<ISiloBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            configureDelegate(AddOrleansCore(hostAppBuilder.Services, hostAppBuilder.Configuration));

            return hostAppBuilder;
        }

        /// <summary>
        /// 配置主机构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostAppBuilder">主机应用程序构建器。</param>
        /// <param name="configureDelegate">用于配置 silo的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="HostApplicationBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// </remarks>
        public static HostApplicationBuilder UseOrleans(
            this HostApplicationBuilder hostAppBuilder,
            Action<ISiloBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            configureDelegate(AddOrleansCore(hostAppBuilder.Services, hostAppBuilder.Configuration));

            return hostAppBuilder;
        }

        /// <summary>
        /// 配置主机构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <param name="configureDelegate">用于配置 silo的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IHostBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// </remarks>
        public static IHostBuilder UseOrleans(
            this IHostBuilder hostBuilder,
            Action<ISiloBuilder> configureDelegate) => hostBuilder.UseOrleans((_, siloBuilder) => configureDelegate(siloBuilder));

        /// <summary>
        /// 配置主机构建器以主机 Orleans silo。
        /// </summary>
        /// <param name="hostBuilder">主机建造者。</param>
        /// <param name="configureDelegate">用于配置 silo的代表。</param>
        /// <returns>主机建造者。</returns>
        /// <remarks>
        /// 在同一 <see cref="IHostBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// </remarks>
        public static IHostBuilder UseOrleans(
            this IHostBuilder hostBuilder,
            Action<HostBuilderContext, ISiloBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            if (hostBuilder.Properties.ContainsKey("HasOrleansClientBuilder"))
            {
                throw GetOrleansClientAddedException();
            }

            hostBuilder.Properties["HasOrleansSiloBuilder"] = "true";

            return hostBuilder.ConfigureServices((context, services) => configureDelegate(context, AddOrleansCore(services, context.Configuration)));
        }

        /// <summary>
        /// 配置服务收藏以主机 Orleans silo。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configureDelegate">用于配置 silo的代表。</param>
        /// <returns>服务收藏。</returns>
        /// <remarks>
        /// 在同一 <see cref="IHostBuilder"/> 实例中多次调用此方法, 将配置一个 silo 。
        /// 然而, <paramref name="configureDelegate"/> 效应将适用于每次通话一次。
        /// </remarks>
        public static IServiceCollection AddOrleans(
            this IServiceCollection services,
            Action<ISiloBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var builder = AddOrleansCore(services, null);

            configureDelegate(builder);

            return services;
        }

        private static ISiloBuilder AddOrleansCore(IServiceCollection services, IConfiguration? configuration)
        {
            ISiloBuilder? builder = default;
            configuration ??= new ConfigurationBuilder().Build();
            foreach (var descriptor in services.Where(d => d.ServiceType.Equals(MarkerType)))
            {
                var marker = (OrleansBuilderMarker)descriptor.ImplementationInstance!;
                builder = marker.BuilderInstance switch
                {
                    ISiloBuilder existingBuilder => existingBuilder,
                    _ => throw GetOrleansClientAddedException()
                };
            }

            if (builder is null)
            {
                builder = new SiloBuilder(services, configuration);
                services.AddSingleton(new OrleansBuilderMarker(builder));
            }

            return builder;
        }

        private static OrleansConfigurationException GetOrleansClientAddedException() =>
            new("Do not call both UseOrleansClient/AddOrleansClient with UseOrleans/AddOrleans. If you want a client and server in the same process, only UseOrleans/AddOrleans is necessary and the UseOrleansClient/AddOrleansClient call can be removed.");
    }
}
