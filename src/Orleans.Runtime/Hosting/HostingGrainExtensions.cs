using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 Silo 上配置 <see cref="IGrainExtension"/> 的方法。
    /// </summary>
    public static class HostingGrainExtensions
    {
        /// <summary>
        /// 为指定接口注册 Grain 扩展实现。
        /// </summary>
        /// <typeparam name="TExtensionInterface">要注册的 <see cref="IGrainExtension"/> 接口。</typeparam>
        /// <typeparam name="TExtension"><typeparamref name="TExtensionInterface"/> 的实现，包括用于依赖注入的公共构造函数。</typeparam>
        public static ISiloBuilder AddGrainExtension<
            TExtensionInterface,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExtension>(
            this ISiloBuilder builder)
            where TExtensionInterface : class, IGrainExtension
            where TExtension : class, TExtensionInterface
        {
            return builder.ConfigureServices(services => services.AddKeyedTransient<IGrainExtension, TExtension>(typeof(TExtensionInterface)));
        }
    }
}
