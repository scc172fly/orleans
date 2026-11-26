using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Providers;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 持久性流的配置功能。
    /// </summary>
    public interface ISiloPersistentStreamConfigurator : IPersistentStreamConfigurator { }

    /// <summary>
    /// <see cref="ISiloPersistentStreamConfigurator"/>的扩展期。
    /// </summary>
    public static class SiloPersistentStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置拖拉机
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigurePullingAgent(this ISiloPersistentStreamConfigurator configurator, Action<OptionsBuilder<StreamPullingAgentOptions>>? configureOptions = null)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置生命周期。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigureLifecycle(this ISiloPersistentStreamConfigurator configurator, Action<OptionsBuilder<StreamLifecycleOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置分区平衡。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">分区平衡工厂</param>
        public static void ConfigurePartitionBalancing(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IStreamQueueBalancer> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置拉动代理器发送信息回端提供者 。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">信息发送后退工厂</param>
        public static void ConfigureBackoffProvider(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IMessageDeliveryBackoffProvider> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置拉动代理器的队列阅读器后端提供者 。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">队列阅读器后退工厂。</param>
        public static void ConfigureBackoffProvider(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IQueueReaderBackoffProvider> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置分区平衡。
        /// </summary>
        /// <typeparam name="TOptions">分区平衡器选项。</typeparam>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">分区平衡工厂</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigurePartitionBalancing<TOptions>(
            this ISiloPersistentStreamConfigurator configurator,
            Func<IServiceProvider, string, IStreamQueueBalancer> factory,
            Action<OptionsBuilder<TOptions>> configureOptions)
            where TOptions : class, new()
        {
            configurator.ConfigureComponent(factory, configureOptions);
        }
    }
}
