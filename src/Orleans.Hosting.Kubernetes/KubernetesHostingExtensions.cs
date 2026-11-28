using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting.Kubernetes;
using Orleans.Runtime;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// 在 Kubernetes 中托管 Silo 的扩展方法。
    /// </summary>
    public static class KubernetesHostingExtensions
    {
        /// <summary>
        /// 添加 Kubernetes 托管支持。
        /// </summary>
        public static ISiloBuilder UseKubernetesHosting(this ISiloBuilder siloBuilder)
        {
            return siloBuilder.ConfigureServices(services => services.UseKubernetesHosting(configureOptions: null));
        }

        /// <summary>
        /// 添加 Kubernetes 托管支持。
        /// </summary>
        public static ISiloBuilder UseKubernetesHosting(this ISiloBuilder siloBuilder, Action<OptionsBuilder<KubernetesHostingOptions>>? configureOptions)
        {
            return siloBuilder.ConfigureServices(services => services.UseKubernetesHosting(configureOptions));
        }

        /// <summary>
        /// 添加 Kubernetes 托管支持。
        /// </summary>
        public static IServiceCollection UseKubernetesHosting(this IServiceCollection services) => services.UseKubernetesHosting(configureOptions: null);

        /// <summary>
        /// 添加 Kubernetes 托管支持。
        /// </summary>
        public static IServiceCollection UseKubernetesHosting(this IServiceCollection services, Action<OptionsBuilder<KubernetesHostingOptions>>? configureOptions)
        {
            configureOptions?.Invoke(services.AddOptions<KubernetesHostingOptions>());

            // 根据当前环境配置默认值。
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
