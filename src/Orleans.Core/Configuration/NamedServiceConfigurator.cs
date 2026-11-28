using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置命名服务的功能。
    /// </summary>
    public interface INamedServiceConfigurator
    {
        /// <summary>
        /// 获取服务名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取用于配置服务的委托。
        /// </summary>
        Action<Action<IServiceCollection>> ConfigureDelegate { get; }
    }

    /// <summary>
    /// 命名服务的组件配置器基类。
    /// 将任何配置或子组件与正在配置的服务关联，并使用相同的名称。
    /// </summary>
    public class NamedServiceConfigurator : INamedServiceConfigurator
    {
        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public Action<Action<IServiceCollection>> ConfigureDelegate { get; }

        /// <summary>
        /// 初始化 <see cref="NamedServiceConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">
        /// 名称。
        /// </param>
        /// <param name="configureDelegate">
        /// 配置委托。
        /// </param>
        public NamedServiceConfigurator(string name, Action<Action<IServiceCollection>> configureDelegate)
        {
            this.Name = name;
            this.ConfigureDelegate = configureDelegate;
        }
    }

    /// <summary>
    /// 用于操作 <see cref="INamedServiceConfigurator"/> 的扩展方法。
    /// </summary>
    public static class NamedServiceConfiguratorExtensions
    {
        /// <summary>
        /// 配置命名服务的选项。
        /// </summary>
        /// <param name="configurator">
        /// 命名服务配置器。
        /// </param>
        /// <param name="configureOptions">
        /// 选项配置委托。
        /// </param>
        /// <typeparam name="TOptions">
        /// 底层选项类型。
        /// </typeparam>
        public static void Configure<TOptions>(this INamedServiceConfigurator configurator, Action<OptionsBuilder<TOptions>>? configureOptions)
            where TOptions : class, new()
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.ConfigureDelegate(services =>
            {
                configureOptions?.Invoke(services.AddOptions<TOptions>(configurator.Name));
                services.ConfigureNamedOptionForLogging<TOptions>(configurator.Name);
            });
        }

        /// <summary>
        /// 向命名服务添加单例组件并配置该服务的选项。
        /// </summary>
        /// <typeparam name="TOptions">正在配置的选项类型。</typeparam>
        /// <typeparam name="TComponent">正在注册的组件服务类型。</typeparam>
        /// <param name="configurator">用于配置组件和选项的命名配置器。</param>
        /// <param name="factory">用于为命名服务创建组件的工厂。</param>
        /// <param name="configureOptions">用于配置命名服务选项的委托。</param>
        public static void ConfigureComponent<TOptions, TComponent>(this INamedServiceConfigurator configurator, Func<IServiceProvider, string, TComponent> factory, Action<OptionsBuilder<TOptions>>? configureOptions = null)
            where TOptions : class, new()
            where TComponent : class
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(factory);

            configurator.Configure(configureOptions);
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 向命名服务添加单例组件。
        /// </summary>
        /// <typeparam name="TComponent">组件服务类型。</typeparam>
        /// <param name="configurator">用于配置组件的命名配置器。</param>
        /// <param name="factory">用于为命名服务创建组件的工厂。</param>
        public static void ConfigureComponent<TComponent>(this INamedServiceConfigurator configurator, Func<IServiceProvider, string, TComponent> factory)
           where TComponent : class
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(factory);

            configurator.ConfigureDelegate(services =>
            {
                services.AddKeyedSingleton<TComponent>(configurator.Name, (sp, key) => factory(sp, (key as string)!));
            });
        }

        /// <summary>
        /// 为命名服务提供生命周期配置点。
        /// </summary>
        /// <typeparam name="T">生命周期主体类型。</typeparam>
        /// <param name="configurator">命名服务配置器。</param>
        public static void ConfigureLifecycle<T>(this INamedServiceConfigurator configurator) where T : ILifecycleSubject
        {

        }
    }
}
