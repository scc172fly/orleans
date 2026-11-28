using System;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 允许提供程序回调其所属提供程序管理器的接口。
    /// 借此可以访问运行时功能，例如日志记录。
    /// </summary>
    public interface IProviderRuntime
    {
        /// <summary>
        /// 获取用于获取 Grain 引用的工厂。
        /// </summary>
        IGrainFactory GrainFactory { get; }

        /// <summary>
        /// 获取用于依赖注入的服务提供程序。
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 如果尚未绑定，则将扩展绑定到可寻址对象。
        /// </summary>
        /// <typeparam name="TExtension">扩展的类型（例如 StreamConsumerExtension）。</typeparam>
        /// <typeparam name="TExtensionInterface">实现的公共接口类型。</typeparam>
        /// <param name="newExtensionFunc">用于创建新扩展对象的工厂函数。</param>
        /// <returns>元组：第一项是扩展，第二项是指向该扩展接口的可寻址引用。</returns>
        (TExtension Extension, TExtensionInterface ExtensionReference) BindExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension;
    }
}
