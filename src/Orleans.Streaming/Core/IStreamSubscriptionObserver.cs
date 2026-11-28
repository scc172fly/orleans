using System.Threading.Tasks;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 由 Grain 实现时，可通知该 Grain 新建或恢复的订阅。
    /// 无状态工作者 Grain 的每个激活实例都会收到此通知，以便各自附加自己的观察者。
    /// </summary>
    public interface IStreamSubscriptionObserver
    {
        /// <summary>
        /// 当此 Grain 收到一个尚未显式订阅或恢复订阅的流的消息时调用。
        /// </summary>
        /// <param name="handleFactory">句柄工厂。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory);
    }
}
