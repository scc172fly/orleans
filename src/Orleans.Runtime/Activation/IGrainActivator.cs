using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 为给定的 Grain 上下文创建 Grain 实例。
    /// </summary>
    public interface IGrainActivator
    {
        /// <summary>
        /// 为提供的 Grain 上下文返回新的 Grain 实例。
        /// </summary>
        /// <param name="context">Grain 上下文。</param>
        /// <returns>Grain 实例。</returns>
        object CreateInstance(IGrainContext context);

        /// <summary>
        /// 释放与提供的 Grain 上下文关联的指定 Grain 实例。
        /// </summary>
        /// <param name="context">Grain 上下文。</param>
        /// <param name="instance">Grain 实例。</param>
        /// <returns>表示所执行工作的 <see cref="ValueTask"/>。</returns>
        ValueTask DisposeInstance(IGrainContext context, object instance);
    }
}
