using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="IAsyncBatchObservable{T}"/> 的扩展方法。
    /// </summary>
    public static class AsyncBatchObservableExtensions
    {
        private static readonly Func<Exception, Task> DefaultOnError = _ => Task.CompletedTask;
        private static readonly Func<Task> DefaultOnCompleted = () => Task.CompletedTask;

        /// <summary>
        /// 使用指定初始位置将消费者订阅到此批量可观察对象。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="observer">要订阅的异步批量观察者。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>表示该订阅的 <see cref="StreamSubscriptionHandle{T}"/> 的异步获取任务。</returns>
        /// <remarks>
        /// <see cref="StreamSubscriptionStartPosition.EarliestAvailable"/> 从本地队列缓存当前为
        /// 目标流保留的最早消息开始。
        /// </remarks>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(
            this IAsyncBatchObservable<T> obs,
            IAsyncBatchObserver<T> observer,
            StreamSubscriptionStartPosition startPosition)
        {
            startPosition.Validate();
            ArgumentNullException.ThrowIfNull(obs);

            if (obs is StreamImpl<T> stream)
            {
                return stream.SubscribeAsync(observer, startPosition);
            }

            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                return obs.SubscribeAsync(observer, null);
            }

            throw new NotSupportedException(
                $"{obs.GetType().FullName} does not support {StreamSubscriptionStartPosition.EarliestAvailable} subscriptions.");
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncBatchObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncBatchObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">用于 IAsyncBatchObserver.OnErrorAsync 回调的委托。</param>
        /// <param name="onCompletedAsync">用于 IAsyncBatchObserver.OnCompletedAsync 回调的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncBatchObservable<T> obs,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           Func<Task> onCompletedAsync)
        {
            var genericObserver = new GenericAsyncBatchObserver<T>(onNextAsync, onErrorAsync, onCompletedAsync);
            ArgumentNullException.ThrowIfNull(obs);

            return obs.SubscribeAsync(genericObserver);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncBatchObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncBatchObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">用于 IAsyncBatchObserver.OnErrorAsync 回调的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncBatchObservable<T> obs,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync)
        {
            return obs.SubscribeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncBatchObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncBatchObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">用于 IAsyncBatchObserver.OnCompletedAsync 回调的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncBatchObservable<T> obs,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, onCompletedAsync);
        }

        /// <summary>
        /// 使用委托将消费者订阅到此可观察对象。
        /// 此方法是 IAsyncBatchObservable.SubscribeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="obs">可观察对象。</param>
        /// <param name="onNextAsync">在 IAsyncBatchObserver.OnNextAsync 时调用的委托。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。</returns>
        public static Task<StreamSubscriptionHandle<T>> SubscribeAsync<T>(this IAsyncBatchObservable<T> obs,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync)
        {
            return obs.SubscribeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted);
        }

    }
}
