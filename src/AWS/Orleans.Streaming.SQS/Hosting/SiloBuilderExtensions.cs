using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于在 Orleans Silo 上配置 Amazon SQS 持久化流的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Silo 使用由 Amazon SQS 支持的持久化流提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureOptions">用于配置 SQS 选项的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddSqsStreams(this ISiloBuilder builder, string name, Action<SqsOptions> configureOptions)
        {
            builder.AddSqsStreams(name, b =>
                b.ConfigureSqs(ob => ob.Configure(configureOptions)));
            return builder;
        }

        /// <summary>
        /// 配置 Silo 使用由 Amazon SQS 支持的持久化流提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddSqsStreams(this ISiloBuilder builder, string name, Action<SiloSqsStreamConfigurator> configure)
        {
            var configurator = new SiloSqsStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
