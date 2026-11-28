using System;

namespace Orleans.Hosting.Developer
{
    /// <summary>
    /// 用于在 Orleans Silo 上配置生成式 Event Hubs 流的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 添加用于开发和测试、可生成 Event Hubs 数据的命名流提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
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
