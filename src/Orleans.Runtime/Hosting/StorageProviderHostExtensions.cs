using System;
using Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Runtime.Hosting
{
    /// <summary>
    /// 登记推广方法 grain 拥有 silo.
    /// </summary>
    public static class StorageProviderExtensions
    {
        /// <summary>
        /// 添加a grain 向 silo. 如果提供者类型安装工具 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>
        /// 它将自动参与 silo 生命周期。
        /// </summary>
        /// <typeparam name="T">具体执行类型 grain 储存提供者。</typeparam>
        /// <param name="collection">服务收藏。</param>
        /// <param name="name">要添加的存储器名称。</param>
        /// <param name="implementationFactory">工厂建造存储供应商。</param>
        /// <returns>服务供应商。</returns>
        public static IServiceCollection AddGrainStorage<T>(this IServiceCollection collection, string name, Func<IServiceProvider, string, T> implementationFactory)
            where T : IGrainStorage
        {
            collection.AddKeyedSingleton<IGrainStorage>(name, (sp, key) => implementationFactory(sp, (string)key!));

            // 检查是否默认执行
            if (string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal))
            {
                collection.TryAddSingleton(sp => sp.GetKeyedService<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)!);
            }

            // 检查 grain 存储器是否安装了 ILifecycleParticipant<ISiloLifecycle>
            if (typeof(ILifecycleParticipant<ISiloLifecycle>).IsAssignableFrom(typeof(T)))
            {
                collection.AddSingleton(s => (ILifecycleParticipant<ISiloLifecycle>)s.GetRequiredKeyedService<IGrainStorage>(name));
            }

            return collection;
        }
    }
}
