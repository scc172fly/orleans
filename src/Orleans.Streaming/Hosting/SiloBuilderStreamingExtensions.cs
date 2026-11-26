using System;
using Orleans.Streams;
using Orleans.Streams.Filtering;

namespace Orleans.Hosting
{
    /// <summary>
    /// silos上的配置流的扩展方法。
    /// </summary>
    public static class SiloBuilderStreamingExtensions
    {
        /// <summary>
        /// 添加对此应用程序流的支持 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddStreaming(this ISiloBuilder builder) => builder.ConfigureServices(services => services.AddSiloStreaming());

        /// <summary>
        /// 配置使用持久性流的 silo 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">提供者名称。</param>
        /// <param name="adapterFactory">供应商适配厂</param>
        /// <param name="configureStream">流体提供者配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddPersistentStreams(
            this ISiloBuilder builder,
            string name,
            Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory,
            Action<ISiloPersistentStreamConfigurator> configureStream)
        {
            //构造器将DI与流的所有默认组件连接起来, 因此无论 configureStream 无效与否, 都需要调用
            var streamConfigurator = new SiloPersistentStreamConfigurator(name, configureDelegate => builder.ConfigureServices(configureDelegate), adapterFactory);
            configureStream?.Invoke(streamConfigurator);
            return builder;
        }

        /// <summary>
        /// 添加流过滤器。 
        /// </summary>
        /// <typeparam name="T">流筛选类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体过滤器名称。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddStreamFilter<T>(this ISiloBuilder builder, string name) where T : class, IStreamFilter
        {
            return builder.ConfigureServices(svc => svc.AddStreamFilter<T>(name));
        }

        /// <summary>
        /// 添加流过滤器。 
        /// </summary>
        /// <typeparam name="T">流筛选类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体过滤器名称。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddStreamFilter<T>(this IClientBuilder builder, string name) where T : class, IStreamFilter
        {
            return builder.ConfigureServices(svc => svc.AddStreamFilter<T>(name));
        }
    }
}
