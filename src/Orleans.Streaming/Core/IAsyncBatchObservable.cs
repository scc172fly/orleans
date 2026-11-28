using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此接口扩展 IAsyncObserver 接口，使生产者和消费者能够批量处理数据项。
    /// <para>
    /// 注意，此接口由数据项的消费者实现，由生产者调用。
    /// 因此，流的消费端实现此接口。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察者消费的对象类型。</typeparam>
    public interface IAsyncBatchObservable<T>
    {
        /// <summary>
        /// 将消费者订阅到此批量可观察对象。
        /// </summary>
        /// <param name="observer">要订阅的异步批量观察者。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer);

        /// <summary>
        /// 将消费者订阅到此批量可观察对象。
        /// </summary>
        /// <param name="observer">要订阅的异步批量观察者。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token);
    }
}
