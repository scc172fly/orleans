using System;
using Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Runtime.Hosting
{
    /// <summary>
    /// 用于向 Silo 注册 Grain 存储提供程序的扩展方法。
    /// </summary>
    public static class StorageProviderExtensions
    {
        /// <summary>
        /// 向 Silo 添加 Grain 存储提供程序实现。如果提供程序类型实现了 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>，
        /// 它将自动参与 Silo 生命周期。
        /// </summary>
        /// <typeparam name="T">Grain 存储提供程序的具体实现类型。</typeparam>
        /// <param name="collection">服务集合。</param>
        /// <param name="name">要添加的存储的名称。</param>
        /// <param name="implementationFactory">用于构建存储提供程序的工厂。</param>
        /// <returns>服务提供程序。</returns>
        public static IServiceCollection AddGrainStorage<T>(this IServiceCollection collection, string name, Func<IServiceProvider, string, T> implementationFactory)
            where T : IGrainStorage
        {
            collection.AddKeyedSingleton<IGrainStorage>(name, (sp, key) => implementationFactory(sp, (string)key!));

            // 检查是否为默认实现。
            if (string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal))
            {
                collection.TryAddSingleton(sp => sp.GetKeyedService<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)!);
            }

            // 检查 Grain 存储是否实现 ILifecycleParticipant<ISiloLifecycle>。
            if (typeof(ILifecycleParticipant<ISiloLifecycle>).IsAssignableFrom(typeof(T)))
            {
                collection.AddSingleton(s => (ILifecycleParticipant<ISiloLifecycle>)s.GetRequiredKeyedService<IGrainStorage>(name));
            }

            return collection;
        }
    }
}
