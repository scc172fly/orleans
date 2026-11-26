using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloBuilder"/> 实例的扩展 。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 添加容器服务。 这可以被多次调用, 结果将是添加 。
        /// </summary>
        /// <param name="builder">要配置的 <see cref="ISiloBuilder" /> 。</param>
        /// <param name="configureDelegate"></param>
        /// <returns>同一案件 <see cref="ISiloBuilder"/> 系链。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configureDelegate"/> 是 <see langword="null"/>。</exception>
        public static ISiloBuilder ConfigureServices(this ISiloBuilder builder, Action<IServiceCollection> configureDelegate)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (configureDelegate == null) throw new ArgumentNullException(nameof(configureDelegate));
            configureDelegate(builder.Services);
            return builder;
        }

        /// <summary>
        /// 注册用于配置特定选项类型的动作。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置选项的动作。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder Configure<TOptions>(this ISiloBuilder builder, Action<TOptions> configureOptions) where TOptions : class
        {
            return builder.ConfigureServices(services => services.Configure(configureOptions));
        }

        /// <summary>
        /// 登记一个 <typeparamref name="TOptions"/> 将约束的配置实例。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configuration">配置。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder Configure<TOptions>(this ISiloBuilder builder, IConfiguration configuration) where TOptions : class
        {
            return builder.ConfigureServices(services => services.AddOptions<TOptions>().Bind(configuration));
        }

        /// <summary>
        /// 添加代表配置所提供的 <see cref="ILoggingBuilder"/>。这可以调用多次。
        /// </summary>
        /// <param name="builder">要配置的 <see cref="ISiloBuilder" /> 。</param>
        /// <param name="configureLogging">配置 <see cref="ILoggingBuilder"/>的代表。</param>
        /// <returns>同一案件 <see cref="ISiloBuilder"/> 系链。</returns>
        public static ISiloBuilder ConfigureLogging(this ISiloBuilder builder, Action<ILoggingBuilder> configureLogging)
        {
            return builder.ConfigureServices(collection => collection.AddLogging(configureLogging));
        }
    }
}
