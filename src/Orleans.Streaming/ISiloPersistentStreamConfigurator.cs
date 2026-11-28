using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Providers;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置持久化流的功能。
    /// </summary>
    public interface ISiloPersistentStreamConfigurator : IPersistentStreamConfigurator { }

    /// <summary>
    /// <see cref="ISiloPersistentStreamConfigurator"/> 的扩展方法。
    /// </summary>
    public static class SiloPersistentStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置拉取代理。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigurePullingAgent(this ISiloPersistentStreamConfigurator configurator, Action<OptionsBuilder<StreamPullingAgentOptions>>? configureOptions = null)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置生命周期。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigureLifecycle(this ISiloPersistentStreamConfigurator configurator, Action<OptionsBuilder<StreamLifecycleOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置分区均衡。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">分区均衡器工厂。</param>
        public static void ConfigurePartitionBalancing(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IStreamQueueBalancer> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置拉取代理的消息投递退避提供程序。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">消息投递退避工厂。</param>
        public static void ConfigureBackoffProvider(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IMessageDeliveryBackoffProvider> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置拉取代理的队列读取退避提供程序。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">队列读取退避工厂。</param>
        public static void ConfigureBackoffProvider(this ISiloPersistentStreamConfigurator configurator, Func<IServiceProvider, string, IQueueReaderBackoffProvider> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置分区均衡。
        /// </summary>
        /// <typeparam name="TOptions">分区均衡器选项。</typeparam>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="factory">分区均衡器工厂。</param>
        /// <param name="configureOptions">配置委托。</param>
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
