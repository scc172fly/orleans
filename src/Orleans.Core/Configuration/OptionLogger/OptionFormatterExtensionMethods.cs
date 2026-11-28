using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 的扩展方法，旨在提升 IOptionFormatter 的易用性。
    /// </summary>
    public static class OptionConfigureExtensionMethods
    {
        /// <summary>
        /// 为 <typeparamref name="TOptions"/> 配置选项格式化器。
        /// </summary>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatter">
        /// 选项格式化器的类型，包括用于依赖注入的公共构造函数。
        /// </typeparam>
        /// <returns>
        /// <see cref="IServiceCollection"/>，用于与其他调用进行链式调用。
        /// </returns>
        public static IServiceCollection ConfigureFormatter<
            TOptions,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TOptionFormatter>(
            this IServiceCollection services)
            where TOptions : class
            where TOptionFormatter : class, IOptionFormatter<TOptions>
        {
            if (services.All(service => service.ServiceType != typeof(IOptionFormatter<TOptions>)))
            {
                services
                    .AddSingleton<IOptionFormatter<TOptions>, TOptionFormatter>()
                    .AddFromExisting<IOptionFormatter, IOptionFormatter<TOptions>>();
            }
            else
            {
                // 覆盖 IOptionFormatter<TOptions> 的服务注册。
                services.AddSingleton<IOptionFormatter<TOptions>, TOptionFormatter>();
            }
            return services;
        }

        /// <summary>
        /// 为 <typeparamref name="TOptions"/> 配置选项格式化器。
        /// </summary>
        /// <remarks>
        /// 除非已配置非默认格式化器，否则使用默认选项格式化器。
        /// </remarks>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <returns>
        /// <see cref="IServiceCollection"/>，用于与其他调用进行链式调用。
        /// </returns>
        public static IServiceCollection ConfigureFormatter<TOptions>(this IServiceCollection services)
            where TOptions : class, new()
        {
            return services.AddSingleton<IOptionFormatter>(sp => sp.GetService<IOptionFormatter<TOptions>>()!);
        }

        /// <summary>
        /// 如果尚未配置选项格式化器，则为 <typeparamref name="TOptions"/> 配置一个。
        /// </summary>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatter">
        /// 选项格式化器的类型，包括用于依赖注入的公共构造函数。
        /// </typeparam>
        /// <returns>
        /// <see cref="IServiceCollection"/>，用于与其他调用进行链式调用。
        /// </returns>
        public static IServiceCollection TryConfigureFormatter<
            TOptions,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TOptionFormatter>(
            this IServiceCollection services)
            where TOptions : class
            where TOptionFormatter : class, IOptionFormatter<TOptions>
        {
            if (services.All(service => service.ServiceType != typeof(IOptionFormatter<TOptions>)))
            {
                services.ConfigureFormatter<TOptions, TOptionFormatter>();
            }

            return services;
        }

        /// <summary>
        /// 为 <typeparamref name="TOptions"/> 配置选项格式化器解析器。
        /// </summary>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatterResolver">
        /// 选项格式化器解析器的类型，包括用于依赖注入的公共构造函数。
        /// </typeparam>
        /// <returns>
        /// <see cref="IServiceCollection"/>，用于与其他调用进行链式调用。
        /// </returns>
        public static IServiceCollection ConfigureFormatterResolver<
            TOptions,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TOptionFormatterResolver>(
            this IServiceCollection services)
            where TOptions : class
            where TOptionFormatterResolver : class, IOptionFormatterResolver<TOptions>
        {
            return services.AddSingleton<IOptionFormatterResolver<TOptions>, TOptionFormatterResolver>();
        }

        /// <summary>
        /// 如果尚未配置，则为命名选项 TOptions 配置选项格式化器解析器。
        /// </summary>
        public static IServiceCollection TryConfigureFormatterResolver<
            TOptions,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TOptionFormatterResolver>(
            this IServiceCollection services)
            where TOptions : class
            where TOptionFormatterResolver : class, IOptionFormatterResolver<TOptions>
        {
            if (services.All(service => service.ServiceType != typeof(IOptionFormatterResolver<TOptions>)))
            {
                return services.ConfigureFormatterResolver<TOptions, TOptionFormatterResolver>();
            }

            return services;
        }

        /// <summary>
        /// 配置要记录到日志的命名选项。
        /// </summary>
        /// <typeparam name="TOptions">
        /// 选项对象的类型。
        /// </typeparam>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <param name="name">
        /// 选项对象的名称。
        /// </param>
        /// <returns>
        /// <see cref="IServiceCollection"/>，用于与其他调用进行链式调用。
        /// </returns>
        public static IServiceCollection ConfigureNamedOptionForLogging<TOptions>(this IServiceCollection services, string name)
            where TOptions : class
        {
            return services.AddSingleton<IOptionFormatter>(sp => sp.GetService<IOptionFormatterResolver<TOptions>>()!.Resolve(name));
        }
    }
}
