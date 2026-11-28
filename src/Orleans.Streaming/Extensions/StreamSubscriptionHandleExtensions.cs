using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="StreamSubscriptionHandle{T}"/> 的扩展方法。
    /// </summary>
    public static class StreamSubscriptionHandleExtensions
    {
        private static readonly Func<Exception, Task> DefaultOnError = _ => Task.CompletedTask;
        private static readonly Func<Task> DefaultOnCompleted = () => Task.CompletedTask;

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            var genericObserver = new GenericAsyncObserver<T>(onNextAsync, onErrorAsync, onCompletedAsync);
            ArgumentNullException.ThrowIfNull(handle);

            return handle.ResumeAsync(genericObserver, token);
        }

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnNextAsync。）</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnErrorAsync。）</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnCompletedAsync。）</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            var genericObserver = new GenericAsyncBatchObserver<T>(onNextAsync, onErrorAsync, onCompletedAsync);
            ArgumentNullException.ThrowIfNull(handle);

            return handle.ResumeAsync(genericObserver, token);
        }

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnNextAsync。）</param>
        /// <param name="onErrorAsync">在 IAsyncObserver.OnErrorAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnErrorAsync。）</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 使用委托恢复流的消费。
        /// 此方法是 StreamSubscriptionHandle.ResumeAsync 的辅助方法，允许订阅类直接内联提供
        /// 处理程序方法，而不必提供 IAsyncBatchObserver 实例。
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnNextAsync。）</param>
        /// <param name="onCompletedAsync">在 IAsyncObserver.OnCompletedAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnCompletedAsync。）</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// <exception cref="ArgumentException">当提供的流筛选函数不适用时抛出。
        /// 通常是因为该函数不是静态方法。</exception>
        /// </summary>
        /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
        /// <param name="handle">订阅句柄。</param>
        /// <param name="onNextAsync">在 IAsyncObserver.OnNextAsync 时调用的委托。（译注：此批量重载实际对应 IAsyncBatchObserver.OnNextAsync。）</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }

    }
}
