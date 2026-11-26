using System;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="IAsyncObservable{T}"/>的推广方法。
    /// </summary>
    public static class AsyncObservableExtensions
    {
        private static readonly Func<Exception, Task> DefaultOnError = _ => Task.CompletedTask;
        private static readonly Func<Task> DefaultOnCompleted = () => Task.CompletedTask;

        /// <summary>
        /// 使用指定的初始位置将消费者加入此可观测的位置。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见的。</param>
        /// <param name="observer">无法同步的订阅观察员 。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <param name="filterData">将传递到过滤器的数据对象。</param>
        /// <returns>承诺 <see cref="StreamSubscriptionHandle{T}"/> 表示订阅。</returns>
        /// <remarks>
        /// <see cref="StreamSubscriptionStartPosition.EarliestAvailable"/> 始于最早的给
        /// 目标流目前保留在本地队列缓存中 。
        /// </remarks>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(
            this IAsyncObservable<T> obs,
            IAsyncObserver<T> observer,
            StreamSubscriptionStartPosition startPosition,
            string? filterData = null)
        {
            startPosition.Validate();
            ArgumentNullException.ThrowIfNull(obs);

            if (obs is StreamImpl<T> stream)
            {
                return stream.SubscribeAsync(observer, startPosition, filterData);
            }

            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                return obs.SubscribeAsync(observer, null, filterData);
            }

            throw new NotSupportedException(
                $"{obs.GetType().FullName} does not support {StreamSubscriptionStartPosition.EarliestAvailable} subscriptions.");
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           Func<Task> onCompletedAsync)
        {
            var genericObserver = new GenericAsyncObserver<T>(onNextAsync, onErrorAsync, onCompletedAsync);
            ArgumentNullException.ThrowIfNull(obs);

            return obs.SubscribeAsync(genericObserver);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync)
        {
            return obs.SubscribeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, onCompletedAsync);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted);
        }


        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken token)
        {
            var genericObserver = new GenericAsyncObserver<T>(onNextAsync, onErrorAsync, onCompletedAsync);
            ArgumentNullException.ThrowIfNull(obs);

            return obs.SubscribeAsync(genericObserver, token);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// 使用代表将消费者加入这一可观测到的用户。
        /// 此方法为 IAsyncObservable.SubscribeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="obs">可见对象。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }
    }
}
