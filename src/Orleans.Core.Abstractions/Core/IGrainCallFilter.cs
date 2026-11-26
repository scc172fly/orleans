using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 进入 grain 调用过滤器的接口。
    /// </summary>
    public interface IIncomingGrainCallFilter
    {
        /// <summary>
        /// 启动此过滤器 。
        /// </summary>
        /// <param name="context">下 grain 调用上下文。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task Invoke(IIncomingGrainCallContext context);
    }

    /// <summary>
    /// 发送 grain 调用过滤器的接口。
    /// </summary>
    public interface IOutgoingGrainCallFilter
    {
        /// <summary>
        /// 启动此过滤器 。
        /// </summary>
        /// <param name="context">下 grain 调用上下文。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task Invoke(IOutgoingGrainCallContext context);
    }
}
