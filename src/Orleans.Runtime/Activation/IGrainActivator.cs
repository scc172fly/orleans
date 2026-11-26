using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 创建 grain 以给定 grain 上下文。
    /// </summary>
    public interface IGrainActivator
    {
        /// <summary>
        /// 返回新 grain 提供的情况 grain 上下文。
        /// </summary>
        /// <param name="context"> grain 上下文。</param>
        /// <returns> grain 实例。</returns>
        object CreateInstance(IGrainContext context);

        /// <summary>
        /// 将规定 grain 与提供的情况有关的案例 grain 上下文。
        /// </summary>
        /// <param name="context"> grain 上下文。</param>
        /// <param name="instance"> grain 实例。</param>
        /// <returns>A <see cref="ValueTask"/> 代表所完成的工作。</returns>
        ValueTask DisposeInstance(IGrainContext context, object instance);
    }
}
