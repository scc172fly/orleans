using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队列均衡器负责在所有其他相关队列均衡器之间对队列进行负载均衡。
    /// 它会将队列分布的变化通知各侦听器（<code>IStreamQueueBalanceListener</code>）。
    /// GetMyQueues、SubscribeToQueueDistributionChangeEvents 和 UnSubscribeFromQueueDistributionChangeEvents 方法
    /// 很可能在 IStreamQueueBalanceListener 的线程中调用，因此这些方法必须是线程安全的。
    /// </summary>
    public interface IStreamQueueBalancer
    {
        /// <summary>
        /// 初始化此实例。
        /// </summary>
        /// <param name="queueMapper">队列映射器。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Initialize(IStreamQueueMapper queueMapper);

        /// <summary>
        /// 关闭队列均衡器。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Shutdown();

        /// <summary>
        /// 获取此均衡器的最新队列分布。
        /// </summary>
        /// <returns>分配给此均衡器的队列。</returns>
        IEnumerable<QueueId> GetMyQueues();

        /// <summary>
        /// 订阅队列分布变化通知。
        /// </summary>
        /// <param name="observer">用于接收队列分布变化通知的观察者接口。</param>
        /// <returns>指示订阅是否成功的值。</returns>
        bool SubscribeToQueueDistributionChangeEvents(IStreamQueueBalanceListener observer);

        /// <summary>
        /// 取消订阅队列分布通知。
        /// </summary>
        /// <param name="observer">用于接收队列分布变化通知的观察者接口。</param>
        /// <returns>指示取消订阅是否成功的值。</returns>
        bool UnSubscribeFromQueueDistributionChangeEvents(IStreamQueueBalanceListener observer);
    }

    /// <summary>
    /// 流队列均衡器侦听器接收来自流队列均衡器（<code>IStreamQueueBalancer</code>）的通知，
    /// 表示队列的均衡分布已发生变化。
    /// 关注流队列负载均衡的组件应实现此接口。
    /// 收到变化通知时，侦听器应向队列均衡器请求更新后的队列列表。
    /// </summary>
    public interface IStreamQueueBalanceListener
    {
        /// <summary>
        /// 适配器负责的队列发生变化时调用。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task QueueDistributionChangeNotification();
    }
}
