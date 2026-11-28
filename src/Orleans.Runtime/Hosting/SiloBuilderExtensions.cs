using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于 <see cref="ISiloBuilder"/> 实例的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 向容器添加服务。可以多次调用此方法，结果将累加。
        /// </summary>
        /// <param name="builder">要配置的 <see cref="ISiloBuilder" />。</param>
        /// <param name="configureDelegate"></param>
        /// <returns>同一个 <see cref="ISiloBuilder"/> 实例，用于链式调用。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configureDelegate"/> 为 <see langword="null"/>。</exception>
        public static ISiloBuilder ConfigureServices(this ISiloBuilder builder, Action<IServiceCollection> configureDelegate)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (configureDelegate == null) throw new ArgumentNullException(nameof(configureDelegate));
            configureDelegate(builder.Services);
            return builder;
        }

        /// <summary>
        /// 注册用于配置特定选项类型的操作。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置选项的操作。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder Configure<TOptions>(this ISiloBuilder builder, Action<TOptions> configureOptions) where TOptions : class
        {
            return builder.ConfigureServices(services => services.Configure(configureOptions));
        }

        /// <summary>
        /// 注册配置实例，供 <typeparamref name="TOptions"/> 进行绑定。
        /// </summary>
        /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configuration">配置。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder Configure<TOptions>(this ISiloBuilder builder, IConfiguration configuration) where TOptions : class
        {
            return builder.ConfigureServices(services => services.AddOptions<TOptions>().Bind(configuration));
        }

        /// <summary>
        /// 添加用于配置所提供的 <see cref="ILoggingBuilder"/> 的委托。可以多次调用。
        /// </summary>
        /// <param name="builder">要配置的 <see cref="ISiloBuilder" />。</param>
        /// <param name="configureLogging">配置 <see cref="ILoggingBuilder"/> 的委托。</param>
        /// <returns>同一个 <see cref="ISiloBuilder"/> 实例，用于链式调用。</returns>
        public static ISiloBuilder ConfigureLogging(this ISiloBuilder builder, Action<ILoggingBuilder> configureLogging)
        {
            return builder.ConfigureServices(collection => collection.AddLogging(configureLogging));
        }
    }
}
