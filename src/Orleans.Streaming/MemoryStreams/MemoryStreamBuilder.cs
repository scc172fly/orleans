using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 内存流的配置构建器。
    /// </summary>
    public interface IMemoryStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 内存流的配置扩展方法。
    /// </summary>
    public static class MemoryStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置内存流的分区。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="numOfQueues">队列数量。</param>
        public static void ConfigurePartitioning(this IMemoryStreamConfigurator configurator, int numOfQueues = HashRingStreamQueueMapperOptions.DEFAULT_NUM_QUEUES)
        {
            configurator.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(options => options.TotalQueueCount = numOfQueues));
        }

        /// <summary>
        /// 为命名的内存流提供程序配置缓存选项。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigureCache(this ISiloMemoryStreamConfigurator configurator, Action<OptionsBuilder<MemoryStreamCacheOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// Silo 专用的内存流配置构建器。
    /// </summary>
    public interface ISiloMemoryStreamConfigurator : IMemoryStreamConfigurator, ISiloRecoverableStreamConfigurator { }

    /// <summary>
    /// 配置内存流。
    /// </summary>
    /// <typeparam name="TSerializer">消息体序列化器类型，必须实现 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
    public class SiloMemoryStreamConfigurator<TSerializer> : SiloRecoverableStreamConfigurator, ISiloMemoryStreamConfigurator
          where TSerializer : class, IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 初始化 <see cref="SiloMemoryStreamConfigurator{TSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">服务配置委托。</param>
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
    /// 客户端专用的内存流配置构建器。
    /// </summary>
    public interface IClusterClientMemoryStreamConfigurator : IMemoryStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 配置内存流。
    /// </summary>
    /// <typeparam name="TSerializer">消息体序列化器类型，必须实现 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
    public class ClusterClientMemoryStreamConfigurator<TSerializer> : ClusterClientPersistentStreamConfigurator, IClusterClientMemoryStreamConfigurator
          where TSerializer : class, IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientMemoryStreamConfigurator{TSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="builder">构建器。</param>
        public ClusterClientMemoryStreamConfigurator(string name, IClientBuilder builder)
         : base(name, builder, MemoryAdapterFactory<TSerializer>.Create)
        {
        }
    }
}
