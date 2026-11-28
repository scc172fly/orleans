using Orleans.Streams.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams.PubSub
{
    /// <summary>
    /// <see cref="IStreamSubscriptionManager"/> 的扩展方法。
    /// </summary>
    public static class StreamSubscriptionManagerExtensions
    {
        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 接口类型。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="grainId">要订阅的 Grain。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            GrainId grainId)
            where TGrainInterface : IGrainWithGuidKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain(grainId) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 作为其主要实现的接口。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">Grain 类名称的前缀。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            Guid primaryKey,
            string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 作为其主要实现的接口。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">Grain 类名称的前缀。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            long primaryKey,
            string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 作为其主要实现的接口。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">Grain 类名称的前缀。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            string primaryKey,
            string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 作为其主要实现的接口。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="keyExtension">Grain 的扩展键。</param>
        /// <param name="grainClassNamePrefix">Grain 类名称的前缀。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            Guid primaryKey,
            string keyExtension,
            string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 将指定 Grain 订阅到指定流。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 作为其主要实现的接口。</typeparam>
        /// <param name="manager">管理器。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="keyExtension">Grain 的扩展键。</param>
        /// <param name="grainClassNamePrefix">Grain 类名称的前缀。</param>
        /// <returns>新添加的订阅。</returns>
        public static Task<StreamSubscription> AddSubscription<TGrainInterface>(
            this IStreamSubscriptionManager manager,
            IGrainFactory grainFactory,
            StreamId streamId,
            string streamProviderName,
            long primaryKey,
            string keyExtension,
            string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(grainFactory);

            var grainRef = grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix) as GrainReference;
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 始终返回派生自 GrainReference 的引用。
        }

        /// <summary>
        /// 返回指定流提供程序的 <see cref="IStreamSubscriptionManager"/>。
        /// </summary>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="manager">管理器。</param>
        /// <returns>如果可以获取流订阅管理器，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        public static bool TryGetStreamSubscriptionManager(this IStreamProvider streamProvider, [NotNullWhen(true)] out IStreamSubscriptionManager? manager)
        {
            manager = null;
            if (streamProvider is IStreamSubscriptionManagerRetriever)
            {
                var streamSubManagerRetriever = streamProvider as IStreamSubscriptionManagerRetriever;
                manager = streamSubManagerRetriever!.GetStreamSubscriptionManager(); // 前面的 `is` 检查保证不为 null；返回值本身可能为 null（仅支持隐式订阅的提供程序）。
                // 仅支持隐式订阅的流提供程序没有配置订阅管理器，
                // 因此 manager 可能为 null。
                return manager != null;
            }
            return false;
        }
    }
}
