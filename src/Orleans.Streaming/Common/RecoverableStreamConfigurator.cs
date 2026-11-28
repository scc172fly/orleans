using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 面向 Silo 的可恢复流配置构建器。
    /// </summary>
    public interface ISiloRecoverableStreamConfigurator : ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// <see cref="ISiloRecoverableStreamConfigurator"/> 的扩展方法。
    /// </summary>
    public static class SiloRecoverableStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置可靠流提供程序的统计选项。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigureStatistics(this ISiloRecoverableStreamConfigurator configurator, Action<OptionsBuilder<StreamStatisticOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置可靠流提供程序的缓存逐出选项。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigureCacheEviction(this ISiloRecoverableStreamConfigurator configurator, Action<OptionsBuilder<StreamCacheEvictionOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 配置可靠流。
    /// </summary>
    public class SiloRecoverableStreamConfigurator : SiloPersistentStreamConfigurator, ISiloRecoverableStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloRecoverableStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureDelegate">配置委托。</param>
        /// <param name="adapterFactory">适配器工厂。</param>
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
