using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting.Kubernetes;
using Orleans.Runtime;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// 托管 silo 内 Kubernetes.
    /// </summary>
    public static class KubernetesHostingExtensions
    {
        /// <summary>
        /// 添加 Kubernetes 主机支持。
        /// </summary>
        public static ISiloBuilder UseKubernetesHosting(this ISiloBuilder siloBuilder)
        {
            return siloBuilder.ConfigureServices(services => services.UseKubernetesHosting(configureOptions: null));
        }

        /// <summary>
        /// 添加 Kubernetes 主机支持。
        /// </summary>
        public static ISiloBuilder UseKubernetesHosting(this ISiloBuilder siloBuilder, Action<OptionsBuilder<KubernetesHostingOptions>>? configureOptions)
        {
            return siloBuilder.ConfigureServices(services => services.UseKubernetesHosting(configureOptions));
        }

        /// <summary>
        /// 添加 Kubernetes 主机支持。
        /// </summary>
        public static IServiceCollection UseKubernetesHosting(this IServiceCollection services) => services.UseKubernetesHosting(configureOptions: null);

        /// <summary>
        /// 添加 Kubernetes 主机支持。
        /// </summary>
        public static IServiceCollection UseKubernetesHosting(this IServiceCollection services, Action<OptionsBuilder<KubernetesHostingOptions>>? configureOptions)
        {
            configureOptions?.Invoke(services.AddOptions<KubernetesHostingOptions>());

            // 基于当前环境配置默认值 。
            services.AddSingleton<IConfigureOptions<ClusterOptions>, ConfigureKubernetesHostingOptions>();
            services.AddSingleton<IConfigureOptions<SiloOptions>, ConfigureKubernetesHostingOptions>();
            services.AddSingleton<IPostConfigureOptions<EndpointOptions>, ConfigureKubernetesHostingOptions>();
            services.AddSingleton<IConfigureOptions<KubernetesHostingOptions>, ConfigureKubernetesHostingOptions>();
            services.AddSingleton<IValidateOptions<KubernetesHostingOptions>, KubernetesHostingOptionsValidator>();

            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, KubernetesClusterAgent>();

            return services;
        }
    }
}
