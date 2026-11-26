using System;

namespace Orleans.Hosting.Developer
{
    /// <summary>
    /// 配置生成事件枢纽流的扩展方法 Orleans silo。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 添加一个名为流提供商的流,生成事件枢纽数据,用于开发和测试。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddEventDataGeneratorStreams(
            this ISiloBuilder builder,
            string name,
            Action<IEventDataGeneratorStreamConfigurator> configure)
        {
            var configurator = new EventDataGeneratorStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
