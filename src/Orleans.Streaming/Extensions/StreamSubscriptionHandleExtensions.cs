using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="StreamSubscriptionHandle{T}"/>的推广方法。
    /// </summary>
    public static class StreamSubscriptionHandleExtensions
    {
        private static readonly Func<Exception, Task> DefaultOnError = _ => Task.CompletedTask;
        private static readonly Func<Task> DefaultOnCompleted = () => Task.CompletedTask;

        /// <summary>
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
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
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<T, StreamSequenceToken?, Task> onNextAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncBatchObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
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
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncBatchObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onErrorAsync">要求派出的代表 IAsyncObserver.OnErrorAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Exception, Task> onErrorAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, onErrorAsync, DefaultOnCompleted, token);
        }

        /// <summary>
        /// 代表使用流的消费恢复。
        /// 此方法为 StreamSubscriptionHandle.ResumeAsync 的帮助器, 允许用户类插入 
        /// 处理器方法,而不是要求使用 IAsyncBatchObserver。
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="onCompletedAsync">要求派出的代表 IAsyncObserver.OnCompletedAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           Func<Task> onCompletedAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, onCompletedAsync, token);
        }

        /// <summary>
        /// <exception cref="ArgumentException">如果所提供的流过滤功能不合适, 丢弃 。 
        /// 通常这是因为这不是静态方法。 </exception>
        /// </summary>
        /// <typeparam name="T">可见观测到的物体类型。</typeparam>
        /// <param name="handle">订阅手柄。</param>
        /// <param name="onNextAsync">要求派出的代表 IAsyncObserver.OnNextAsync。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        public static Task<StreamSubscriptionHandle<T>> ResumeAsync<T>(this StreamSubscriptionHandle<T> handle,
                                                                           Func<IList<SequentialItem<T>>, Task> onNextAsync,
                                                                           StreamSequenceToken? token = null)
        {
            return handle.ResumeAsync(onNextAsync, DefaultOnError, DefaultOnCompleted, token);
        }

    }
}
