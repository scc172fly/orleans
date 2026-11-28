using System.Threading.Tasks;

namespace Orleans.Providers
{
    /// <summary>
    /// Orleans 运行时内部可控制组件的通用接口。
    /// </summary>
    public interface IControllable
    {
        /// <summary>
        /// 用于执行控制命令的函数。
        /// </summary>
        /// <param name="command">命令序号。</param>
        /// <param name="arg">不透明的命令参数。</param>
        /// <returns>命令处理程序返回的值。</returns>
        Task<object?> ExecuteCommand(int command, object? arg);
    }
}
