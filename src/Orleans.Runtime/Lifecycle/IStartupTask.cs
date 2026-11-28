using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 定义 Silo 启动后要执行的操作。
    /// </summary>
    public interface IStartupTask
    {
        /// <summary>
        /// Silo 启动后调用。
        /// </summary>
        /// <param name="cancellationToken">当方法必须中止时被取消的取消令牌。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task Execute(CancellationToken cancellationToken);
    }
}
