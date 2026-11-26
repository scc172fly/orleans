using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Storage;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloPersistentStreamConfigurator"/>的推广方法。
    /// </summary>
    public static class SiloPersistentStreamConfiguratorExtension
    {
        /// <summary>
        /// 配置流提供方使用一致的环队列平衡器。
        /// </summary>
        /// <param name="configurator">和弦师</param>
        public static void UseConsistentRingQueueBalancer(this ISiloPersistentStreamConfigurator configurator)
        {
            configurator.ConfigurePartitionBalancing(ConsistentRingQueueBalancer.Create);
        }

        /// <summary>
        /// 配置流提供方使用静态集束配置部署平衡器的配置。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="siloMaturityPeriod">下 silo 到期日。</param>
        public static void UseStaticClusterConfigDeploymentBalancer(
            this ISiloPersistentStreamConfigurator configurator,
            TimeSpan? siloMaturityPeriod = null)
        {
            configurator.ConfigurePartitionBalancing<DeploymentBasedQueueBalancerOptions>(
                (s, n) => DeploymentBasedQueueBalancer.Create(s, n, s.GetRequiredService<IOptions<StaticClusterDeploymentOptions>>().Value),
                options => options.Configure(op =>
                {
                    op.IsFixed = true;
                    if (siloMaturityPeriod.HasValue)
                        op.SiloMaturityPeriod = siloMaturityPeriod.Value;
                }));
        }

        /// <summary>
        /// 配置流提供方使用动态集束配置部署平衡器。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="siloMaturityPeriod">下 silo 到期日。</param>
        public static void UseDynamicClusterConfigDeploymentBalancer(
            this ISiloPersistentStreamConfigurator configurator,
            TimeSpan? siloMaturityPeriod = null)
        {
            configurator.ConfigurePartitionBalancing<DeploymentBasedQueueBalancerOptions>(
                (s, n) => DeploymentBasedQueueBalancer.Create(s, n, s.GetRequiredService<IOptions<StaticClusterDeploymentOptions>>().Value),
                options => options.Configure(op =>
                {
                    op.IsFixed = false;
                    if (siloMaturityPeriod.HasValue)
                        op.SiloMaturityPeriod = siloMaturityPeriod.Value;
                }));
        }

        /// <summary>
        /// 配置流提供方使用租赁的队列平衡器。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置选项。</param>
        public static void UseLeaseBasedQueueBalancer(this ISiloPersistentStreamConfigurator configurator,
            Action<OptionsBuilder<LeaseBasedQueueBalancerOptions>>? configureOptions = null)
        {
            configurator.ConfigureComponent((s, n) => LeaseBasedQueueBalancer.Create(s, n),
                configureOptions);
        }

        /// <summary>
        /// 配置流提供方使用 grain- 基于检查器的流提供方。
        /// </summary>
        /// <remarks>
        /// 使用 <c>PubSubStore</c> grain 存储提供者,检查站一直保持。
        /// </remarks>
        /// <param name="configurator">配置构建器。</param>
        public static void UseGrainCheckpointer(this ISiloPersistentStreamConfigurator configurator)
        {
            UseGrainCheckpointer(configurator, configureOptions: null);
        }

        /// <summary>
        /// 配置流提供方使用 grain- 基于检查器的流提供方。
        /// </summary>
        /// <remarks>
        /// 配置的 grain 存储提供者必须向 silo注册。
        /// 每个流提供者可选择不同的 grain 存储提供者。
        /// </remarks>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions"> grain 检查站配置。</param>
        public static void UseGrainCheckpointer(
            this ISiloPersistentStreamConfigurator configurator,
            Action<OptionsBuilder<GrainStreamQueueCheckpointerOptions>>? configureOptions)
        {
            configurator.ConfigureComponent<GrainStreamQueueCheckpointerOptions, IStreamQueueCheckpointerFactory>(
                GrainStreamQueueCheckpointerFactory.CreateFactory,
                options =>
                {
                    options.Validate(
                        static value => value.PersistInterval > TimeSpan.Zero,
                        $"{nameof(GrainStreamQueueCheckpointerOptions.PersistInterval)} must be greater than zero.");
                    options.Validate(
                        static value => !string.IsNullOrWhiteSpace(value.StorageProviderName),
                        $"{nameof(GrainStreamQueueCheckpointerOptions.StorageProviderName)} is required.");
                    options.Validate<IServiceProviderIsKeyedService>(
                        static (value, services) => services.IsKeyedService(
                            typeof(IGrainStorage),
                            value.StorageProviderName),
                        $"{nameof(GrainStreamQueueCheckpointerOptions.StorageProviderName)} must identify a registered grain storage provider.");
                    options.ValidateOnStart();
                    configureOptions?.Invoke(options);
                });
        }
    }
}
