using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 这个界面对 IAsyncObserver 接口进行统称,允许生产批次的物品和消费。
    /// <para>
    /// 请注意,该接口由物品消费者实施,并由物品生产者援引(使用)。
    /// 这意味着流的消费端点执行此接口 。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察员消费的物体类型。</typeparam>
    public interface IAsyncBatchObservable<T>
    {
        /// <summary>
        /// 将消费者订入这批可观测到的物品。
        /// </summary>
        /// <param name="observer">无法同步的批量观察者订阅 。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer);

        /// <summary>
        /// 将消费者订入这批可观测到的物品。
        /// </summary>
        /// <param name="observer">无法同步的批量观察者订阅 。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token);
    }
}
