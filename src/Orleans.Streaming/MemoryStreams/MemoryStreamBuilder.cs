using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 内存流配置构建器 。
    /// </summary>
    public interface IMemoryStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 内存流配置扩展 。
    /// </summary>
    public static class MemoryStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置内存流的分区。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="numOfQueues">队列数。</param>
        public static void ConfigurePartitioning(this IMemoryStreamConfigurator configurator, int numOfQueues = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
        {
            configurator.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = numOfQueues));
        }

        /// <summary>
        /// 配置命名的内存流提供者的缓存选项。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置代表。</param>
        public static void ConfigureCache(this ISiloMemoryStreamConfigurator configurator, Action<OptionsBuilder<MemoryStreamCacheOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// Silo- 内存流的配置构建器。
    /// </summary>
    public interface ISiloMemoryStreamConfigurator : IMemoryStreamConfigurator, ISiloRecoverableStreamConfigurator { }

    /// <summary>
    /// 配置内存流。
    /// </summary>
    /// <typeparam name="TSerializer">电文正文序列器类型,必须执行 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
    public class SiloMemoryStreamConfigurator<TSerializer> : SiloRecoverableStreamConfigurator, ISiloMemoryStreamConfigurator
          where TSerializer : class, IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 初始化 <see cref="SiloMemoryStreamConfigurator{TSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureServicesDelegate">服务配置代表。</param>
        public SiloMemoryStreamConfigurator(
            string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, MemoryAdapterFactory<TSerializer>.Create)
        {
            this.ConfigureDelegate(services => services
                .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                .ConfigureNamedOptionForLogging<MemoryStreamCacheOptions>(name));
            this.Configure<MemoryStreamCacheOptions>(options => options
                .Validate(
                    static value => value.MaxAddCount > 0,
                    $"{nameof(MemoryStreamCacheOptions.MaxAddCount)} must be greater than zero.")
                .ValidateOnStart());
        }
    }

    /// <summary>
    /// 用于记忆流的客户特有配置构建器。
    /// </summary>
    public interface IClusterClientMemoryStreamConfigurator : IMemoryStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 配置内存流。
    /// </summary>
    /// <typeparam name="TSerializer">电文正文序列器类型,必须执行 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
    public class ClusterClientMemoryStreamConfigurator<TSerializer> : ClusterClientPersistentStreamConfigurator, IClusterClientMemoryStreamConfigurator
          where TSerializer : class, IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientMemoryStreamConfigurator{TSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="builder">建筑工。</param>
        public ClusterClientMemoryStreamConfigurator(string name, IClientBuilder builder)
         : base(name, builder, MemoryAdapterFactory<TSerializer>.Create)
        {
        }
    }
}
