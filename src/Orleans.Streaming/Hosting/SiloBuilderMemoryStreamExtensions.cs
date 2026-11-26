using System;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloBuilder"/> 用于配置模拟流的扩展方法。 
    /// </summary>
    public static class SiloBuilderMemoryStreamExtensions
    {

        /// <summary>
        /// 配置 silo 使用默认信件序列器使用内存流
        /// (<see cref="DefaultMemoryMessageBodySerializer"/>).
        /// </summary>
        /// 使用默认内置序列器
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddMemoryStreams(this ISiloBuilder builder, string name,
                Action<ISiloMemoryStreamConfigurator>? configure = null)
        {
            return AddMemoryStreams<DefaultMemoryMessageBodySerializer>(builder, name, configure);
        }

        /// <summary>
        /// 配置 silo 使用内存流。
        /// </summary>
        /// <typeparam name="TSerializer">信件序列器类型,必须执行 <see cref="IMemoryMessageBodySerializer"/>。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddMemoryStreams<TSerializer>(this ISiloBuilder builder, string name,
            Action<ISiloMemoryStreamConfigurator>? configure = null)
             where TSerializer : class, IMemoryMessageBodySerializer
        {
            //构造器将DI与流的所有默认组件连接起来, 因此无论 configureStream 无效与否, 都需要调用
            var memoryStreamConfiguretor = new SiloMemoryStreamConfigurator<TSerializer>(name,
                configureDelegate => builder.ConfigureServices(configureDelegate)
            );
            configure?.Invoke(memoryStreamConfiguretor);
            return builder;
        }
    }
}
