using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.GrainDirectory;
using Orleans.Hosting;

namespace Orleans.Runtime.Hosting
{
    /// <summary>
    /// 用于向 Silo 注册 Grain 目录实现的扩展方法。
    /// </summary>
    public static class DirectorySiloBuilderExtensions
    {
        /// <summary>
        /// 向 Silo 添加 Grain 目录提供程序实现。如果提供程序类型实现了 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>，
        /// 它将自动参与 Silo 生命周期。
        /// </summary>
        /// <typeparam name="T">Grain 目录提供程序的具体实现类型。</typeparam>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">要添加的 Grain 目录的名称。</param>
        /// <param name="implementationFactory">用于构建 Grain 目录提供程序的工厂。</param>
        /// <returns>Silo 构建器。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="implementationFactory"/> 为 <see langword="null"/>。</exception>
        public static ISiloBuilder AddGrainDirectory<T>(this ISiloBuilder builder, string name, Func<IServiceProvider, string, T> implementationFactory)
            where T : class, IGrainDirectory
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddGrainDirectory<T>(name, implementationFactory);
            return builder;
        }

        /// <summary>
        /// 向 Silo 添加 Grain 目录提供程序实现。如果提供程序类型实现了 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>，
        /// 它将自动参与 Silo 生命周期。
        /// </summary>
        /// <typeparam name="T">Grain 目录提供程序的具体实现类型。</typeparam>
        /// <param name="collection">服务集合。</param>
        /// <param name="name">要添加的 Grain 目录的名称。</param>
        /// <param name="implementationFactory">用于构建 Grain 目录提供程序的工厂。</param>
        /// <returns>服务集合。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="collection"/> 或 <paramref name="implementationFactory"/> 为 <see langword="null"/>。</exception>
        public static IServiceCollection AddGrainDirectory<T>(this IServiceCollection collection, string name, Func<IServiceProvider, string, T> implementationFactory)
            where T : class, IGrainDirectory
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(implementationFactory);
            if (name == GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY)
            {
                collection.RemoveDefaultGrainDirectory();
            }

            var registration = collection.AddGrainDirectoryAlias(name, implementationFactory);
            registration.LifecycleParticipant = ServiceDescriptor.Singleton<ILifecycleParticipant<ISiloLifecycle>>(s =>
                s.GetKeyedService<IGrainDirectory>(name) as ILifecycleParticipant<ISiloLifecycle> ?? NoOpLifecycleParticipant.Instance);
            collection.Add(registration.LifecycleParticipant);

            return collection;
        }

        internal static NamedService<IGrainDirectory> AddGrainDirectoryAlias<T>(
            this IServiceCollection collection,
            string name,
            Func<IServiceProvider, string, T> implementationFactory)
            where T : class, IGrainDirectory
        {
            // 注册 Grain 目录名称，使目录可以按名称枚举。
            var registration = new NamedService<IGrainDirectory>(name, typeof(T));
            collection.AddSingleton(registration);

            // 注册 Grain 目录实现。
            collection.AddKeyedSingleton<IGrainDirectory>(name, (sp, _) => implementationFactory(sp, name));
            return registration;
        }

        internal static void RemoveDefaultGrainDirectory(this IServiceCollection collection)
        {
            for (var index = collection.Count - 1; index >= 0; index--)
            {
                if (!collection[index].IsKeyedService
                    && collection[index].ImplementationInstance is NamedService<IGrainDirectory> registration
                    && registration.Name == GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY)
                {
                    collection.RemoveAt(index);
                    if (registration.LifecycleParticipant is { } lifecycleParticipant)
                    {
                        collection.Remove(lifecycleParticipant);
                    }
                }
            }

            collection.RemoveAllKeyed<IGrainDirectory>(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY);
            CoreHostingExtensions.RemoveUnusedDistributedGrainDirectory(collection);
        }

        internal static IEnumerable<NamedService<IGrainDirectory>> GetGrainDirectories(this IServiceProvider serviceProvider)
        {
            return serviceProvider.GetServices<NamedService<IGrainDirectory>>() ?? [];
        }

        private sealed class NoOpLifecycleParticipant : ILifecycleParticipant<ISiloLifecycle>
        {
            public static readonly NoOpLifecycleParticipant Instance = new();

            public void Participate(ISiloLifecycle observer) { }
        }
    }
}
