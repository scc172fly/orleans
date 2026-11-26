using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队列平衡器负责在所有其他相关的队列平衡器中加载均衡队列。
    /// 在队列分布变化时通知所有监听器（<code>IStreamQueueBalanceListener</code>）。
    /// 方法 GetMyQueues、 SubscribeToQueueDistributionChangeEvents和 UnSubscribeFromQueueDistributionChangeEvents 将 
    /// 可能被调入 IStreamQueueBalanceListener线串, 以便它们需要安全
    /// </summary>
    public interface IStreamQueueBalancer
    {
        /// <summary>
        /// 初始化了这个实例。
        /// </summary>
        /// <param name="queueMapper">队列映像仪。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Initialize(IStreamQueueMapper queueMapper);

        /// <summary>
        /// 关闭队列平衡器
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Shutdown();

        /// <summary>
        /// 获取此平衡器的最新队列分布 。
        /// </summary>
        /// <returns>分配给此平衡器的队列 。</returns>
        IEnumerable<QueueId> GetMyQueues();

        /// <summary>
        /// 订阅接收队列分配更改通知
        /// </summary>
        /// <param name="observer">一个接收队列分配变更通知的观察界面。</param>
        /// <returns>表示订阅成功与否的值。</returns>
        bool SubscribeToQueueDistributionChangeEvents(IStreamQueueBalanceListener observer);

        /// <summary>
        /// 不接受队列分布通知的订阅。
        /// </summary>
        /// <param name="observer">一个接收队列分配变更通知的观察界面。</param>
        /// <returns>表示 Teh 取消订阅是否成功</returns>的数值
        bool UnSubscribeFromQueueDistributionChangeEvents(IStreamQueueBalanceListener observer);
    }

    /// <summary>
    /// 流队列均衡监听器接收流队列均衡器（<code>IStreamQueueBalancer</code>）发出的通知
    /// 显示队列的平衡已经改变。
    /// 实施时应当由关心排队量平衡的构成部分组成。
    /// 收到更改通知后,听众应请求从队列平衡器中获取最新的队列列表。
    /// </summary>
    public interface IStreamQueueBalanceListener
    {
        /// <summary>
        /// 当适应者队列责任改变时呼叫 。 
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task QueueDistributionChangeNotification();
    }
}
