using Orleans.Serialization.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace Orleans.Serialization
{
    /// <summary>
    /// <see cref="ISerializerBuilder"/>的扩展期。
    /// </summary>
    public static class SerializerBuilderExtensions
    {
        private static readonly object _assembliesKey = new();

        /// <summary>
        /// 将上下文的封闭类型图添加到序列式分辨率中。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="context">生成的上下文。</param>
        /// <returns>序列构建器。</returns>
        public static ISerializerBuilder AddGeneratedContext(this ISerializerBuilder builder, GeneratedContext context)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (context is null) throw new ArgumentNullException(nameof(context));
            return builder.Configure(context);
        }

        /// <summary>
        /// 配置序列构建器。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="factory">工厂</param>
        /// <returns>序列号构建器</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static ISerializerBuilder Configure(this ISerializerBuilder builder, Func<IServiceProvider, IConfigureOptions<TypeManifestOptions>> factory)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            builder.Services.AddSingleton<IConfigureOptions<TypeManifestOptions>>(factory);
            return builder;
        }

        /// <summary>
        /// 配置序列构建器。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>序列号构建器</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static ISerializerBuilder Configure(this ISerializerBuilder builder, IConfigureOptions<TypeManifestOptions> configure)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            builder.Services.AddSingleton<IConfigureOptions<TypeManifestOptions>>(configure);
            return builder;
        }

        /// <summary>
        /// 配置序列构建器。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configure">配置代表。</param>
        /// <returns>序列号构建器</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static ISerializerBuilder Configure(this ISerializerBuilder builder, Action<TypeManifestOptions> configure)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            builder.Services.Configure(configure);
            return builder;
        }

        /// <summary>
        /// 向构建者添加一个组件。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="assembly">组装。</param>
        /// <returns>序列号构建器</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static ISerializerBuilder AddAssembly(this ISerializerBuilder builder, Assembly assembly)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            var attrs = assembly.GetCustomAttributes<TypeManifestProviderAttribute>();

            foreach (var attr in attrs)
            {
                _ = builder.Services.AddSingleton(typeof(IConfigureOptions<TypeManifestOptions>), attr.ProviderType);
            }

            return builder;
        }
    }
}
