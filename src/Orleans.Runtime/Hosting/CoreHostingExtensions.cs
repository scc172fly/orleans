using System;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.GrainDirectory;
using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Hosting;
using Orleans.Runtime.MembershipService;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于 <see cref="ISiloBuilder"/> 实例的扩展方法。
    /// </summary>
    public static class CoreHostingExtensions
    {
        // 运行时拥有的描述符标识将别名注册、生命周期参与和选择不使用该功能时的清理关联在一起。
        private static readonly ServiceDescriptor DirectoryDescriptor = ServiceDescriptor.Singleton<DistributedGrainDirectory, DistributedGrainDirectory>();
        private static readonly ServiceDescriptor DirectoryLifecycleDescriptor =
            ServiceDescriptor.Singleton<ILifecycleParticipant<ISiloLifecycle>>(static sp => sp.GetRequiredService<DistributedGrainDirectory>());
        private static readonly ServiceDescriptor DirectoryMembershipDescriptor = ServiceDescriptor.Singleton(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<GrainDirectoryOptions>>().Value;
            return new DirectoryMembershipService(
                sp.GetRequiredService<ClusterMembershipService>(),
                sp.GetRequiredService<IInternalGrainFactory>(),
                sp.GetRequiredService<ILogger<DirectoryMembershipService>>(),
                options.PartitionsPerSilo,
                options.GetPartitionBoundaries);
        });

        /// <summary>
        /// 添加通过 Grain 调用传播 <see cref="Activity.Current"/> 的支持。
        /// 注意：根据 <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>，仅在存在 Activity 监听器 <see cref="ActivitySource.HasListeners()"/> 且 <see cref="ActivityListener.Sample"/> 返回 <see cref="ActivitySamplingResult.PropagationData"/> 时才会创建 Activity。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <returns>构建器。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
        public static ISiloBuilder AddActivityPropagation(this ISiloBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton(DistributedContextPropagator.Current);

            return builder
                .AddOutgoingGrainCallFilter<ActivityPropagationOutgoingGrainCallFilter>()
                .AddIncomingGrainCallFilter<ActivityPropagationIncomingGrainCallFilter>();
        }

        /// <summary>
        /// 将 Silo 配置为使用仅供开发的集群模式，并在 localhost 上监听。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="siloPort">Silo 端口。</param>
        /// <param name="gatewayPort">网关端口。</param>
        /// <param name="primarySiloEndpoint">
        /// 主 Silo 的终结点，或使用 <see langword="null"/> 将当前 Silo 作为主 Silo。
        /// </param>
        /// <param name="serviceId">服务标识。</param>
        /// <param name="clusterId">集群标识。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseLocalhostClustering(
            this ISiloBuilder builder,
            int siloPort = EndpointOptions.DEFAULT_SILO_PORT,
            int gatewayPort = EndpointOptions.DEFAULT_GATEWAY_PORT,
            IPEndPoint? primarySiloEndpoint = null,
            string serviceId = ClusterOptions.DevelopmentServiceId,
            string clusterId = ClusterOptions.DevelopmentClusterId)
        {
            builder.Configure<EndpointOptions>(options =>
            {
                options.AdvertisedIPAddress = IPAddress.Loopback;
                options.SiloPort = siloPort;
                options.GatewayPort = gatewayPort;
            });

            builder.UseDevelopmentClustering(optionsBuilder => ConfigurePrimarySiloEndpoint(optionsBuilder, primarySiloEndpoint));
            builder.ConfigureServices(services =>
            {
                // 如果调用方未覆盖服务标识或集群标识，则配置默认值作为回退值。
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

            return builder;
        }

        /// <summary>
        /// 将 Silo 配置为使用仅供开发的集群模式。
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="primarySiloEndpoint">
        /// 主 Silo 的终结点，或使用 <see langword="null"/> 将当前 Silo 作为主 Silo。
        /// </param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseDevelopmentClustering(this ISiloBuilder builder, IPEndPoint? primarySiloEndpoint)
        {
            return builder.UseDevelopmentClustering(optionsBuilder => ConfigurePrimarySiloEndpoint(optionsBuilder, primarySiloEndpoint));
        }

        /// <summary>
        /// 将 Silo 配置为使用仅供开发的集群模式。
        /// </summary>
        public static ISiloBuilder UseDevelopmentClustering(
            this ISiloBuilder builder,
            Action<DevelopmentClusterMembershipOptions> configureOptions)
        {
            return builder.UseDevelopmentClustering(options => options.Configure(configureOptions));
        }

        /// <summary>
        /// 将 Silo 配置为使用仅供开发的集群模式。
        /// </summary>
        public static ISiloBuilder UseDevelopmentClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<DevelopmentClusterMembershipOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<DevelopmentClusterMembershipOptions>());
                    services.ConfigureFormatter<DevelopmentClusterMembershipOptions>();
                    services
                        .AddSingleton<SystemTargetBasedMembershipTable>()
                        .AddFromExisting<IMembershipTable, SystemTargetBasedMembershipTable>();
                    services
                        .AddSingleton<MembershipTableSystemTarget>()
                        .AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, MembershipTableSystemTarget>();
                });
        }

        private static void ConfigurePrimarySiloEndpoint(OptionsBuilder<DevelopmentClusterMembershipOptions> optionsBuilder, IPEndPoint? primarySiloEndpoint)
        {
            optionsBuilder.Configure((DevelopmentClusterMembershipOptions options, IOptions<EndpointOptions> endpointOptions) =>
            {
                if (primarySiloEndpoint is null)
                {
                    primarySiloEndpoint = endpointOptions.Value.GetPublicSiloEndpoint();
                }

                options.PrimarySiloEndpoint = primarySiloEndpoint;
            });
        }

        /// <summary>
        /// 为未显式选择目录的 Grain 类型选用旧版 Grain 目录作为默认目录。
        /// </summary>
        /// <param name="siloBuilder">Silo 构建器。</param>
        /// <returns>提供的 Silo 构建器。</returns>
        /// <remarks>
        /// 为每个 Silo 配置此选项以推迟迁移到分布式 Grain 目录。
        /// 从旧版目录迁移到分布式目录是单向的。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="siloBuilder"/> 为 <see langword="null"/>。</exception>
        public static ISiloBuilder UseLegacyGrainDirectory(this ISiloBuilder siloBuilder)
        {
            ArgumentNullException.ThrowIfNull(siloBuilder);
            siloBuilder.Services.RemoveDefaultGrainDirectory();
            return siloBuilder;
        }

        internal static void RemoveUnusedDistributedGrainDirectory(IServiceCollection services)
        {
            foreach (var service in services)
            {
                if (!service.IsKeyedService
                    && service.ImplementationInstance is NamedService<IGrainDirectory> registration
                    && registration.ImplementationType == typeof(DistributedGrainDirectory))
                {
                    return;
                }
            }

            // 移除未使用的分布式目录服务，以便旧版目录安装其兼容性终结点。
            services.Remove(DirectoryDescriptor);
            services.Remove(DirectoryLifecycleDescriptor);
            services.Remove(DirectoryMembershipDescriptor);
        }

        /// <summary>
        /// 配置分布式 Grain 目录。
        /// </summary>
        /// <param name="siloBuilder">用于注册目录实现的 Silo 构建器。</param>
        /// <param name="name">注册目录时使用的名称，或使用 null 将其注册为默认目录。多个名称共享一个目录实例。</param>
        /// <returns>提供的 Silo 构建器。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="siloBuilder"/> 为 <see langword="null"/>。</exception>
        public static ISiloBuilder AddDistributedGrainDirectory(this ISiloBuilder siloBuilder, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(siloBuilder);
            var services = siloBuilder.Services;
            if (string.IsNullOrEmpty(name))
            {
                name = GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY;
            }

            if (name == GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY)
            {
                services.RemoveDefaultGrainDirectory();
            }

            // 分布式 Grain 目录。
            services.AddOptions<GrainDirectoryOptions>();
            services.TryAdd(DirectoryMembershipDescriptor);
            if (!services.Contains(DirectoryDescriptor))
            {
                services.Add(DirectoryDescriptor);
                services.Add(DirectoryLifecycleDescriptor);
            }

            services.AddGrainDirectoryAlias<DistributedGrainDirectory>(
                name,
                static (sp, _) => sp.GetRequiredService<DistributedGrainDirectory>());
            return siloBuilder;
        }
    }
}
