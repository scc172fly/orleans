using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.GrainDirectory;
using Orleans.Hosting;

namespace Orleans.Runtime.Hosting
{
    /// <summary>
    /// 用于登记 grain 目录实施情况的扩展方法,并使用 silo。
    /// </summary>
    public static class DirectorySiloBuilderExtensions
    {
        /// <summary>
        /// 将 grain 目录提供者的实施添加到 silo。如果提供者类型执行 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>
        /// 它将自动参与 silo 生命周期。
        /// </summary>
        /// <typeparam name="T">具体执行类型 grain 目录提供者。</typeparam>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">要添加的 grain 目录的名称。</param>
        /// <param name="implementationFactory">建造 grain 目录提供者。</param>
        /// <returns>下 silo 建筑商</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="implementationFactory"/> 是 <see langword="null"/>。</exception>
        public static ISiloBuilder AddGrainDirectory<T>(this ISiloBuilder builder, string name, Func<IServiceProvider, string, T> implementationFactory)
            where T : class, IGrainDirectory
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddGrainDirectory<T>(name, implementationFactory);
            return builder;
        }

        /// <summary>
        /// 将 grain 目录提供者的实施添加到 silo。如果提供者类型执行 <see cref="ILifecycleParticipant{ISiloLifecycle}"/>
        /// 它将自动参与 silo 生命周期。
        /// </summary>
        /// <typeparam name="T">具体执行类型 grain 目录提供者。</typeparam>
        /// <param name="collection">服务收藏。</param>
        /// <param name="name">要添加的 grain 目录的名称。</param>
        /// <param name="implementationFactory">建造 grain 目录提供者。</param>
        /// <returns>服务收藏。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="collection"/> 或 <paramref name="implementationFactory"/> 是 <see langword="null"/>。</exception>
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
            // 注册 grain 目录名称,以便目录能够按名称列出。
            var registration = new NamedService<IGrainDirectory>(name, typeof(T));
            collection.AddSingleton(registration);

            // 登记 grain 目录实施情况。
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
