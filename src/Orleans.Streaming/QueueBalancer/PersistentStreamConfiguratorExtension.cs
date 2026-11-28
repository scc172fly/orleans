using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Storage;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloPersistentStreamConfigurator"/> 的扩展方法。
    /// </summary>
    public static class SiloPersistentStreamConfiguratorExtension
    {
        /// <summary>
        /// 将流提供程序配置为使用一致性哈希环队列均衡器。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        public static void UseConsistentRingQueueBalancer(this ISiloPersistentStreamConfigurator configurator)
        {
            configurator.ConfigurePartitionBalancing(ConsistentRingQueueBalancer.Create);
        }

        /// <summary>
        /// 将流提供程序配置为使用基于静态集群配置的部署均衡器。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="siloMaturityPeriod">Silo 成熟期。</param>
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
        /// 将流提供程序配置为使用基于动态集群配置的部署均衡器。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="siloMaturityPeriod">Silo 成熟期。</param>
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
        /// 将流提供程序配置为使用基于租约的队列均衡器。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">用于配置选项的操作。</param>
        public static void UseLeaseBasedQueueBalancer(this ISiloPersistentStreamConfigurator configurator,
            Action<OptionsBuilder<LeaseBasedQueueBalancerOptions>>? configureOptions = null)
        {
            configurator.ConfigureComponent((s, n) => LeaseBasedQueueBalancer.Create(s, n),
                configureOptions);
        }

        /// <summary>
        /// 将流提供程序配置为使用基于 Grain 的检查点管理器。
        /// </summary>
        /// <remarks>
        /// 检查点通过 <c>PubSubStore</c> Grain 存储提供程序持久化。
        /// </remarks>
        /// <param name="configurator">配置构建器。</param>
        public static void UseGrainCheckpointer(this ISiloPersistentStreamConfigurator configurator)
        {
            UseGrainCheckpointer(configurator, configureOptions: null);
        }

        /// <summary>
        /// 将流提供程序配置为使用基于 Grain 的检查点管理器。
        /// </summary>
        /// <remarks>
        /// 配置的 Grain 存储提供程序必须在 Silo 中注册。
        /// 每个流提供程序可以选择不同的 Grain 存储提供程序。
        /// </remarks>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">Grain 检查点管理器配置。</param>
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
