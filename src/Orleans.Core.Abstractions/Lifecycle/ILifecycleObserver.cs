using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 用于处理启动和停止通知的生命周期观察者。
    /// </summary>
    public interface ILifecycleObserver
    {
        /// <summary>
        /// 处理启动通知。
        /// </summary>
        /// <param name="cancellationToken">
        /// 取消时指示操作应尽快中止的取消令牌。
        /// </param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task OnStart(CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理停止通知。
        /// </summary>
        /// <param name="cancellationToken">
        /// 取消时指示操作应尽快停止的取消令牌。
        /// </param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task OnStop(CancellationToken cancellationToken = default);
    }
}
