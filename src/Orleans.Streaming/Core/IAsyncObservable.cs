
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此界面对标准 .NET IO可观测接口进行通用,以便允许物品的同步消耗。
    /// 这里的不同步性意味着消费者可以不同步地处理物品,并信号完成物品 
    /// 制片人完成返回的任务。
    /// <para>
    /// 请注意,该接口被物品消费者所援引(使用),并由物品生产者加以实施。
    /// 这意味着流的生产者端点执行此接口 。
    /// </para>
    /// </summary>
    /// <typeparam name="T">可见观测到的物体类型。</typeparam>
    public interface IAsyncObservable<T>
    {
        /// <summary>
        /// 将消费者加入这一可见的商品。
        /// </summary>
        /// <param name="observer">无法同步的订阅观察员 。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer);

        /// <summary>
        /// 将消费者加入这一可见的商品。
        /// </summary>
        /// <param name="observer">无法同步的订阅观察员 。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <param name="filterData">数据对象将传递到过滤器。</param>
        /// <returns>承诺签署 StreamSubscriptionHandle ,表示订阅。
        /// 消费者可以使用此把手取消订阅。
        /// 只要不是明确未订阅,订阅活动就一直有效。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token, string? filterData = null);
    }
}
