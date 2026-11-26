using Orleans.Streams.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams.PubSub
{
    /// <summary>
    /// <see cref="IStreamSubscriptionManager"/>的推广方法。
    /// </summary>
    public static class StreamSubscriptionManagerExtensions
    {
        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface"> grain 接口类型。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="grainId">下 grain 要订阅。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface">界面的界面 grain 它们是主要执行。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="primaryKey"> grain主密钥。</param>
        /// <param name="grainClassNamePrefix"> grain 类名称前缀。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface">界面的界面 grain 它们是主要执行。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="primaryKey"> grain主密钥。</param>
        /// <param name="grainClassNamePrefix"> grain 类名称前缀。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface">界面的界面 grain 它们是主要执行。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="primaryKey"> grain主密钥。</param>
        /// <param name="grainClassNamePrefix"> grain 类名称前缀。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface">界面的界面 grain 它们是主要执行。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="primaryKey"> grain主密钥。</param>
        /// <param name="keyExtension"> grain的密钥扩展名。</param>
        /// <param name="grainClassNamePrefix"> grain 类名称前缀。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 将指定的 grain 订阅到指定的流。
        /// </summary>
        /// <typeparam name="TGrainInterface">界面的界面 grain 它们是主要执行。</typeparam>
        /// <param name="manager">经理</param>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="primaryKey"> grain主密钥。</param>
        /// <param name="keyExtension"> grain的密钥扩展名。</param>
        /// <param name="grainClassNamePrefix"> grain 类名称前缀。</param>
        /// <returns>新增加的订阅。</returns>
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
            return manager.AddSubscription(streamProviderName, streamId, grainRef!); // GetGrain 总是返回 GrainReference- 源参考。
        }

        /// <summary>
        /// 返回提供流提供者的 <see cref="IStreamSubscriptionManager"/> 。
        /// </summary>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="manager">经理</param>
        /// <returns><see langword="true" /> 如果可以检索流订阅管理器, <see langword="false" /> 否则。</returns>
        public static bool TryGetStreamSubscriptionManager(this IStreamProvider streamProvider, [NotNullWhen(true)] out IStreamSubscriptionManager? manager)
        {
            manager = null;
            if (streamProvider is IStreamSubscriptionManagerRetriever)
            {
                var streamSubManagerRetriever = streamProvider as IStreamSubscriptionManagerRetriever;
                manager = streamSubManagerRetriever!.GetStreamSubscriptionManager(); // 由前一次 `is` 检查保证不中标;返回值本身可能是无效的(仅含暗号提供者)。
                //只有隐含流流提供方没有配置订阅管理器 
                //使经理人可以无效;
                return manager != null;
            }
            return false;
        }
    }
}
