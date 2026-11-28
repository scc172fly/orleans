using System;
using Orleans.Streams;
using Orleans.Streams.Filtering;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 Silo 上配置流处理的扩展方法。
    /// </summary>
    public static class SiloBuilderStreamingExtensions
    {
        /// <summary>
        /// 为此应用程序添加流处理支持。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddStreaming(this ISiloBuilder builder) => builder.ConfigureServices(services => services.AddSiloStreaming());

        /// <summary>
        /// 将 Silo 配置为使用持久化流。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="adapterFactory">提供程序适配器工厂。</param>
        /// <param name="configureStream">流提供程序配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddPersistentStreams(
            this ISiloBuilder builder,
            string name,
            Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory,
            Action<ISiloPersistentStreamConfigurator> configureStream)
        {
            // 构造函数会将流的所有默认组件注册到依赖注入容器，因此无论 configureStream 是否为 null，都需要调用该构造函数。
            var streamConfigurator = new SiloPersistentStreamConfigurator(name, configureDelegate => builder.ConfigureServices(configureDelegate), adapterFactory);
            configureStream?.Invoke(streamConfigurator);
            return builder;
        }

        /// <summary>
        /// 添加流筛选器。
        /// </summary>
        /// <typeparam name="T">流筛选器类型。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流筛选器名称。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddStreamFilter<T>(this ISiloBuilder builder, string name) where T : class, IStreamFilter
        {
            return builder.ConfigureServices(svc => svc.AddStreamFilter<T>(name));
        }

        /// <summary>
        /// 添加流筛选器。
        /// </summary>
        /// <typeparam name="T">流筛选器类型。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流筛选器名称。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddStreamFilter<T>(this IClientBuilder builder, string name) where T : class, IStreamFilter
        {
            return builder.ConfigureServices(svc => svc.AddStreamFilter<T>(name));
        }
    }
}
