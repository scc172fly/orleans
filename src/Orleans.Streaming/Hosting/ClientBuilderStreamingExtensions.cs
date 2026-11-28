using System;
using Orleans.Providers;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 Orleans 客户端上配置流处理的扩展方法。
    /// </summary>
    public static class ClientBuilderStreamingExtensions
    {
        /// <summary>
        /// 为此客户端添加流处理支持。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddStreaming(this IClientBuilder builder) => builder.ConfigureServices(services => services.AddClientStreaming());

        /// <summary>
        /// 使用默认消息序列化器为客户端添加新的内存流提供程序。
        /// (<see cref="DefaultMemoryMessageBodySerializer"/>).
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">配置委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddMemoryStreams(
            this IClientBuilder builder,
            string name,
            Action<IClusterClientMemoryStreamConfigurator>? configure = null)
        {
            return AddMemoryStreams<DefaultMemoryMessageBodySerializer>(builder, name, configure);
        }

        /// <summary>
        /// 为客户端添加新的内存流提供程序。
        /// </summary>
        /// <typeparam name="TSerializer">序列化器的类型。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">配置委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddMemoryStreams<TSerializer>(
            this IClientBuilder builder,
            string name,
            Action<IClusterClientMemoryStreamConfigurator>? configure = null)
            where TSerializer : class, IMemoryMessageBodySerializer
        {
            // 构造函数会将流的所有默认组件注册到依赖注入容器，因此无论 configureStream 是否为 null，都需要调用该构造函数。
            var memoryStreamConfigurator = new ClusterClientMemoryStreamConfigurator<TSerializer>(name, builder);
            configure?.Invoke(memoryStreamConfigurator);
            return builder;
        }

        /// <summary>
        /// 为客户端添加新的持久化流提供程序。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="adapterFactory">适配器工厂。</param>
        /// <param name="configureStream">配置委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddPersistentStreams(
            this IClientBuilder builder,
            string name,
            Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory,
            Action<IClusterClientPersistentStreamConfigurator>? configureStream)
        {
            // 构造函数会将流的所有默认组件注册到依赖注入容器，因此无论 configureStream 是否为 null，都需要调用该构造函数。
            var streamConfigurator = new ClusterClientPersistentStreamConfigurator(name, builder, adapterFactory);
            configureStream?.Invoke(streamConfigurator);
            return builder;
        }
    }
}
