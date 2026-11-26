using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 使用生命周期观察员处理起止通知。
    /// </summary>
    public interface ILifecycleObserver
    {
        /// <summary>
        /// 处理启动通知 。
        /// </summary>
        /// <param name="cancellationToken">
        /// 取消符, 表示取消时操作应该迅速中止 。
        /// </param>
        /// <returns>
        /// 代表操作的 <see cref="Task"/> 。
        /// </returns>
        Task OnStart(CancellationToken cancellationToken = default);

        /// <summary>
        /// 处理停止通知。
        /// </summary>
        /// <param name="cancellationToken">
        /// 取消符, 表示取消时操作应该被迅速停止 。
        /// </param>
        /// <returns>
        /// 代表操作的 <see cref="Task"/> 。
        /// </returns>
        Task OnStop(CancellationToken cancellationToken = default);
    }
}
