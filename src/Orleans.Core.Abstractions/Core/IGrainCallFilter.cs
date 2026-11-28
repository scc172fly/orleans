using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 传入 Grain 调用过滤器的接口。
    /// </summary>
    public interface IIncomingGrainCallFilter
    {
        /// <summary>
        /// 调用此过滤器。
        /// </summary>
        /// <param name="context">Grain 调用上下文。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task Invoke(IIncomingGrainCallContext context);
    }

    /// <summary>
    /// 传出 Grain 调用过滤器的接口。
    /// </summary>
    public interface IOutgoingGrainCallFilter
    {
        /// <summary>
        /// 调用此过滤器。
        /// </summary>
        /// <param name="context">Grain 调用上下文。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task Invoke(IOutgoingGrainCallContext context);
    }
}
