using System;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置内存流的 <see cref="ISiloBuilder"/> 扩展方法。
    /// </summary>
    public static class SiloBuilderMemoryStreamExtensions
    {

        /// <summary>
        /// 使用默认消息序列化器将 Silo 配置为使用内存流。
        /// (<see cref="DefaultMemoryMessageBodySerializer"/>).
        /// </summary>
        /// 使用默认内置序列化器。
        /// <param name="builder">构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryStreams(this ISiloBuilder builder, string name,
                Action<ISiloMemoryStreamConfigurator>? configure = null)
        {
            return AddMemoryStreams<DefaultMemoryMessageBodySerializer>(builder, name, configure);
        }

        /// <summary>
        /// 将 Silo 配置为使用内存流。
        /// </summary>
        /// <typeparam name="TSerializer">消息序列化器类型，必须实现 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryStreams<TSerializer>(this ISiloBuilder builder, string name,
            Action<ISiloMemoryStreamConfigurator>? configure = null)
             where TSerializer : class, IMemoryMessageBodySerializer
        {
            // 构造函数会将流的所有默认组件注册到依赖注入容器，因此无论 configureStream 是否为 null，都需要调用该构造函数。
            var memoryStreamConfiguretor = new SiloMemoryStreamConfigurator<TSerializer>(name,
                configureDelegate => builder.ConfigureServices(configureDelegate)
            );
            configure?.Invoke(memoryStreamConfiguretor);
            return builder;
        }
    }
}
