
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此接口扩展标准 .NET IObservable 接口，支持异步消费数据项。
    /// 此处的异步指消费者可以异步处理数据项，并通过完成返回的 Task
    /// 向生产者通知数据项已处理完成。
    /// <para>
    /// 注意，此接口由数据项的生产者实现，由消费者调用。
    /// 因此，流的生产端实现此接口。
    /// </para>
    /// </summary>
    /// <typeparam name="T">可观察对象生成的对象类型。</typeparam>
    public interface IAsyncObservable<T>
    {
        /// <summary>
        /// 将消费者订阅到此可观察对象。
        /// </summary>
        /// <param name="observer">要订阅的异步观察者。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer);

        /// <summary>
        /// 将消费者订阅到此可观察对象。
        /// </summary>
        /// <param name="observer">要订阅的异步观察者。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <param name="filterData">传递给筛选器的数据对象。</param>
        /// <returns>表示该订阅的 StreamSubscriptionHandle 的异步获取任务。
        /// 使用此句柄，消费者可以取消订阅。
        /// 只要未显式取消订阅，订阅就会保持活动状态。
        /// </returns>
        Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token, string? filterData = null);
    }
}
