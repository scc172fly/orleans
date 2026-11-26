using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置方法 <see cref="IGrainExtension"/>秒对a silo.
    /// </summary>
    public static class HostingGrainExtensions
    {
        /// <summary>
        /// 登记指定接口的 grain 扩展执行。
        /// </summary>
        /// <typeparam name="TExtensionInterface"> <see cref="IGrainExtension"/> 接口正在登记。</typeparam>
        /// <typeparam name="TExtension">执行《执行和执行执行执行 <typeparamref name="TExtensionInterface"/>包括用于依赖注射的公共建筑师。</typeparam>
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
