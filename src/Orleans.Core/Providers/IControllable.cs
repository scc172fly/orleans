using System.Threading.Tasks;

namespace Orleans.Providers
{
    /// <summary>
    /// 在 Orleans 运行时用于控制组件的通用界面。
    /// </summary>
    public interface IControllable
    {
        /// <summary>
        /// 函数来执行控制命令。
        /// </summary>
        /// <param name="command">命令的序列号。</param>
        /// <param name="arg">一个不透明的命令参数。</param>
        /// <returns>命令处理器返回的值。</returns>
        Task<object?> ExecuteCommand(int command, object? arg);
    }
}
