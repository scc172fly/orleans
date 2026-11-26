using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo- 可回收流的配置构建器。
    /// </summary>
    public interface ISiloRecoverableStreamConfigurator : ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// <see cref="ISiloRecoverableStreamConfigurator"/>的推广方法。
    /// </summary>
    public static class SiloRecoverableStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置可靠流提供者的统计选项。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigureStatistics(this ISiloRecoverableStreamConfigurator configurator, Action<OptionsBuilder<StreamStatisticOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置一个可靠的流提供商的缓存驱逐选项 。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigureCacheEviction(this ISiloRecoverableStreamConfigurator configurator, Action<OptionsBuilder<StreamCacheEvictionOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 配置可靠的流。
    /// </summary>
    public class SiloRecoverableStreamConfigurator : SiloPersistentStreamConfigurator, ISiloRecoverableStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloRecoverableStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureDelegate">配置代表。</param>
        /// <param name="adapterFactory">适配厂</param>
        public SiloRecoverableStreamConfigurator(
            string name,
            Action<Action<IServiceCollection>> configureDelegate,
            Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory)
            : base(name, configureDelegate, adapterFactory)
        {
            this.ConfigureDelegate(services => services
                .ConfigureNamedOptionForLogging<StreamStatisticOptions>(name)
                .ConfigureNamedOptionForLogging<StreamCacheEvictionOptions>(name));
        }
    }
}
