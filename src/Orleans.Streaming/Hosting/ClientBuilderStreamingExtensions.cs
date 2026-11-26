using System;
using Orleans.Providers;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Orleans 客户端流的扩展方法。
    /// </summary>
    public static class ClientBuilderStreamingExtensions
    {
        /// <summary>
        /// 添加向此客户端流的支持 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddStreaming(this IClientBuilder builder) => builder.ConfigureServices(services => services.AddClientStreaming());

        /// <summary>
        /// 使用默认信件序列器向客户端添加一个新的内线流提供商
        /// (<see cref="DefaultMemoryMessageBodySerializer"/>).
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddMemoryStreams(
            this IClientBuilder builder,
            string name,
            Action<IClusterClientMemoryStreamConfigurator>? configure = null)
        {
            return AddMemoryStreams<DefaultMemoryMessageBodySerializer>(builder, name, configure);
        }

        /// <summary>
        /// 向客户端添加一个新的内线流提供者。
        /// </summary>
        /// <typeparam name="TSerializer">t 序列器的类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddMemoryStreams<TSerializer>(
            this IClientBuilder builder,
            string name,
            Action<IClusterClientMemoryStreamConfigurator>? configure = null)
            where TSerializer : class, IMemoryMessageBodySerializer
        {
            //构造器将DI与流的所有默认组件连接起来, 因此无论 configureStream 无效与否, 都需要调用
            var memoryStreamConfigurator = new ClusterClientMemoryStreamConfigurator<TSerializer>(name, builder);
            configure?.Invoke(memoryStreamConfigurator);
            return builder;
        }

        /// <summary>
        /// 向客户端添加新的持续流提供者。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="adapterFactory">适配厂</param>
        /// <param name="configureStream">配置代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddPersistentStreams(
            this IClientBuilder builder,
            string name,
            Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory,
            Action<IClusterClientPersistentStreamConfigurator>? configureStream)
        {
            //构造器将DI与流的所有默认组件连接起来, 因此无论 configureStream 无效与否, 都需要调用
            var streamConfigurator = new ClusterClientPersistentStreamConfigurator(name, builder, adapterFactory);
            configureStream?.Invoke(streamConfigurator);
            return builder;
        }
    }
}
