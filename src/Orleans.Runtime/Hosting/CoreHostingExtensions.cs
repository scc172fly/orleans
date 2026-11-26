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
    /// <see cref="ISiloBuilder"/> 实例的扩展 。
    /// </summary>
    public static class CoreHostingExtensions
    {
        // 使用时间自主的描述性身份将别名登记、生命周期参与和选择退出清理结合在一起。
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
        /// 添加添加添加 <see cref="Activity.Current"/> 通过 grain 电话
        /// 注:根据 <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> 活动,只有在存在任何活动听众时才会创建 <see cref="ActivitySource.HasListeners()"/> 和 <see cref="ActivityListener.Sample"/> 返回 <see cref="ActivitySamplingResult.PropagationData"/>。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static ISiloBuilder AddActivityPropagation(this ISiloBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton(DistributedContextPropagator.Current);

            return builder
                .AddOutgoingGrainCallFilter<ActivityPropagationOutgoingGrainCallFilter>()
                .AddIncomingGrainCallFilter<ActivityPropagationIncomingGrainCallFilter>();
        }

        /// <summary>
        /// 配置 silo ,以使用开发专用组合和收听本地主机。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="siloPort"> silo 端口。</param>
        /// <param name="gatewayPort">网关端口。</param>
        /// <param name="primarySiloEndpoint">
        /// 初级 silo或 <see langword="null"/> 的终点,将此 silo 作为初级。
        /// </param>
        /// <param name="serviceId">服务代号。</param>
        /// <param name="clusterId">组号 ID 。</param>
        /// <returns>下 silo 建筑商</returns>
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

            return builder;
        }

        /// <summary>
        /// 配置 silo 使用只开发的组合。
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="primarySiloEndpoint">
        /// 初级 silo或 <see langword="null"/> 的终点,将此 silo 作为初级。
        /// </param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseDevelopmentClustering(this ISiloBuilder builder, IPEndPoint? primarySiloEndpoint)
        {
            return builder.UseDevelopmentClustering(optionsBuilder => ConfigurePrimarySiloEndpoint(optionsBuilder, primarySiloEndpoint));
        }

        /// <summary>
        /// 配置 silo 使用只开发的组合。
        /// </summary>
        public static ISiloBuilder UseDevelopmentClustering(
            this ISiloBuilder builder,
            Action<DevelopmentClusterMembershipOptions> configureOptions)
        {
            return builder.UseDevelopmentClustering(options => options.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 silo 使用只开发的组合。
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
        /// 选择遗留 grain 目录作为 grain 类型中的默认值,无需明确选择目录。
        /// </summary>
        /// <param name="siloBuilder">下 silo 建筑商</param>
        /// <returns>提供的情况 silo 建筑商</returns>
        /// <remarks>
        /// 配置每个 silo , 并在此选项下推迟迁移到分布的 grain 目录 。
        /// 从遗留目录迁移到分布目录是单向的。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="siloBuilder"/> 是 <see langword="null"/>.</exception>
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

            // 删除未使用的分布式服务后,遗留目录可以安装兼容端点。
            services.Remove(DirectoryDescriptor);
            services.Remove(DirectoryLifecycleDescriptor);
            services.Remove(DirectoryMembershipDescriptor);
        }

        /// <summary>
        /// 配置分布的 grain 目录。
        /// </summary>
        /// <param name="siloBuilder">用于登记目录实施情况的 silo 构建器。</param>
        /// <param name="name">将目录注册在下面或空, 将其登记为默认。多个名称共享一个目录实例 。</param>
        /// <returns>提供的情况 silo 建筑商</returns>
        /// <exception cref="ArgumentNullException"><paramref name="siloBuilder"/> 是 <see langword="null"/>.</exception>
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

            // 分发 Grain 目录
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
