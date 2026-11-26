using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 定义在启动 silo 后要采取的行动。
    /// </summary>
    public interface IStartupTask
    {
        /// <summary>
        /// 启动 silo 后调用。
        /// </summary>
        /// <param name="cancellationToken">当方法中止时取消的取消符号。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task Execute(CancellationToken cancellationToken);
    }
}
