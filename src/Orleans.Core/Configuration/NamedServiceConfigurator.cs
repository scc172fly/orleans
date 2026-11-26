using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置指定服务的职能性。
    /// </summary>
    public interface INamedServiceConfigurator
    {
        /// <summary>
        /// 获得服务名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取代表用来配置服务。
        /// </summary>
        Action<Action<IServiceCollection>> ConfigureDelegate { get; }
    }

    /// <summary>
    /// 地名服务配置器基本基级
    /// 此功能将任何配置或子组件连接到与配置的服务同名的配置或子部件
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
        /// 名字 名字 名字 名字 The name.
        /// </param>
        /// <param name="configureDelegate">
        /// 组合代表
        /// </param>
        public NamedServiceConfigurator(string name, Action<Action<IServiceCollection>> configureDelegate)
        {
            this.Name = name;
            this.ConfigureDelegate = configureDelegate;
        }
    }

    /// <summary>
    /// 与 <see cref="INamedServiceConfigurator"/>合作的延长期。
    /// </summary>
    public static class NamedServiceConfiguratorExtensions
    {
        /// <summary>
        /// 配置指定服务的选项。
        /// </summary>
        /// <param name="configurator">
        /// 命名服务配置器
        /// </param>
        /// <param name="configureOptions">
        /// 选项配置代表 。
        /// </param>
        /// <typeparam name="TOptions">
        /// 基本选项类型。
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
        /// 将单吨组件添加到指定服务中,并为指定服务配置选项。
        /// </summary>
        /// <typeparam name="TOptions">正在配置的选项类型。</typeparam>
        /// <typeparam name="TComponent">正在注册的组件服务类型。</typeparam>
        /// <param name="configurator">组件和选项的配置将为此配置的名称配置。</param>
        /// <param name="factory">工厂用来为命名服务创建组件。</param>
        /// <param name="configureOptions">代表用来配置指定服务的选项。</param>
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
        /// 将单吨元件添加到指定服务中。
        /// </summary>
        /// <typeparam name="TComponent">构成部分服务类型。</typeparam>
        /// <param name="configurator">组件要配置的命名配置器。</param>
        /// <param name="factory">工厂用来为命名服务创建组件。</param>
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
        /// 提供命名服务的生命周期配置点。
        /// </summary>
        /// <typeparam name="T">生命周期主题类型。</typeparam>
        /// <param name="configurator">命名服务配置器。</param>
        public static void ConfigureLifecycle<T>(this INamedServiceConfigurator configurator) where T : ILifecycleSubject
        {

        }
    }
}
