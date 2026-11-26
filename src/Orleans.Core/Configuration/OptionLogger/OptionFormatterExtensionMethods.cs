using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="IServiceCollection"/>的扩展方法,使供应商更适合使用 IOptionFormatter。
    /// </summary>
    public static class OptionConfigureExtensionMethods
    {
        /// <summary>
        /// 配置 <typeparamref name="TOptions"/>的选项。
        /// </summary>
        /// <param name="services">
        /// 服务。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatter">
        /// 选择物质类型,包括用于依赖注射的公共建筑师。
        /// </typeparam>
        /// <returns>
        /// 下 <see cref="IServiceCollection"/>用来绑住其他电话
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
                // 覆盖 IOptionFormatter<TOptions>
                services.AddSingleton<IOptionFormatter<TOptions>, TOptionFormatter>();
            }
            return services;
        }

        /// <summary>
        /// 配置 <typeparamref name="TOptions"/>的选项。
        /// </summary>
        /// <remarks>
        /// 这将使用默认选项来处理事项, 除非配置了非默认的事项 。
        /// </remarks>
        /// <param name="services">
        /// 服务。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <returns>
        /// 下 <see cref="IServiceCollection"/>用来绑住其他电话
        /// </returns>
        public static IServiceCollection ConfigureFormatter<TOptions>(this IServiceCollection services)
            where TOptions : class, new()
        {
            return services.AddSingleton<IOptionFormatter>(sp => sp.GetService<IOptionFormatter<TOptions>>()!);
        }

        /// <summary>
        /// 配置尚未配置的 <typeparamref name="TOptions"/> 的选项。
        /// </summary>
        /// <param name="services">
        /// 服务。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatter">
        /// 选择物质类型,包括用于依赖注射的公共建筑师。
        /// </typeparam>
        /// <returns>
        /// 下 <see cref="IServiceCollection"/>用来绑住其他电话
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
        /// 配置 <typeparamref name="TOptions"/>的物料解答器选项。
        /// </summary>
        /// <param name="services">
        /// 服务。
        /// </param>
        /// <typeparam name="TOptions">
        /// 选项类型。
        /// </typeparam>
        /// <typeparam name="TOptionFormatterResolver">
        /// 用于物料解答器类型的选项,包括用于依赖注射的公共建筑师。
        /// </typeparam>
        /// <returns>
        /// 下 <see cref="IServiceCollection"/>用来绑住其他电话
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
        /// 配置指定选项 Toptions 的物料解解器配置选项, 如果未配置
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
        /// 配置要登录的命名选项 。
        /// </summary>
        /// <typeparam name="TOptions">
        /// 选项对象的类型。
        /// </typeparam>
        /// <param name="services">
        /// 服务。
        /// </param>
        /// <param name="name">
        /// 选项对象的名称。
        /// </param>
        /// <returns>
        /// 下 <see cref="IServiceCollection"/>用来绑住其他电话
        /// </returns>
        public static IServiceCollection ConfigureNamedOptionForLogging<TOptions>(this IServiceCollection services, string name)
            where TOptions : class
        {
            return services.AddSingleton<IOptionFormatter>(sp => sp.GetService<IOptionFormatterResolver<TOptions>>()!.Resolve(name));
        }
    }
}
