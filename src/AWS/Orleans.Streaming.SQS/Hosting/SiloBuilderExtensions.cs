using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供扩展方法,用于配置 Orleans silos的亚马逊 SQS支持的持久性流。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 使用 Amazon SQS 支持的持久性流提供商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configureOptions">用于配置 SQS 选项的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddSqsStreams(this ISiloBuilder builder, string name, Action<SqsOptions> configureOptions)
        {
            builder.AddSqsStreams(name, b =>
                b.ConfigureSqs(ob => ob.Configure(configureOptions)));
            return builder;
        }

        /// <summary>
        /// 配置 silo 使用 Amazon SQS 支持的持久性流提供商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddSqsStreams(this ISiloBuilder builder, string name, Action<SiloSqsStreamConfigurator> configure)
        {
            var configurator = new SiloSqsStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
