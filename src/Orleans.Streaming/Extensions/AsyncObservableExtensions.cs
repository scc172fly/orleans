using System;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="IAsyncObservable{T}"/> 的扩展方法。
    /// </summary>
    public static class AsyncObservableExtensions
    {
        private static readonly Func<Exception, Task> DefaultOnError = _ => Task.CompletedTask;
        private static readonly Func<Task> DefaultOnCompleted = () => Task.CompletedTask;

        /// <summary>
        /// 使用指定初始位置将消费者订阅到此可观察对象。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="observer">要订阅的异步观察者。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <param name="filterData">传递给筛选器的数据对象。</param>
        /// <returns>表示该订阅的 <see cref="StreamSubscriptionHandle{T}"/> 的异步获取任务。</returns>
        /// <remarks>
        /// <see cref="StreamSubscriptionStartPosition.EarliestAvailable"/> 从本地队列缓存当前为
        /// 目标流保留的最早消息开始。
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
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
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
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync)
        {
            return obs.SubscribeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, onCompletedAsync);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted);
        }


        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
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
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncObservable<T> obs,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           StreamSequenceToken token)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }
    }
}
