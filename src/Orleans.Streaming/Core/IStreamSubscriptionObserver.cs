using System.Threading.Tasks;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 当由 grain执行时,通知任何新的或恢复的订阅 grain 。
    /// 无国籍工人 grains 收到每次激活时的通知,这样每个启动都可附上自己的观察员。
    /// </summary>
    public interface IStreamSubscriptionObserver
    {
        /// <summary>
        /// 当此 grain 收到一条尚未明确签署或恢复的流信息时调用 。
        /// </summary>
        /// <param name="handleFactory">把手柄厂</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory);
    }
}
