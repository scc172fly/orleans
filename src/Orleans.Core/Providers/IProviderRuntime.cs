using System;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 接口,允许供应商回调到指定提供商-管理员中。
    /// 这样可以进入运行时功能,例如记录。
    /// </summary>
    public interface IProviderRuntime
    {
        /// <summary>
        /// 获取 grains引用的工厂。
        /// </summary>
        IGrainFactory GrainFactory { get; }

        /// <summary>
        /// 获得服务供应商的自援注射。
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 将扩展部分绑在可标定对象的扩展面上,如果尚未完成的话。
        /// </summary>
        /// <typeparam name="TExtension">扩展名类型(e.g. StreamConsumerExtension).</typeparam>
        /// <typeparam name="TExtensionInterface">执行的公共接口类型。</typeparam>
        /// <param name="newExtensionFunc">制造新扩展对象的工厂函数。</param>
        /// <returns>象牙,包含第一个扩展名,第二个可处理的扩展名界面引用。</returns>
        (TExtension Extension, TExtensionInterface ExtensionReference) BindExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension;
    }
}
