using System;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 用于 <see cref="IGrainContext"/> 且与 <see cref="IGrainExtension"/> 相关的扩展方法。
    /// </summary>
    public static class GrainContextComponentExtensions
    {
        /// <summary>
        /// 由 <see cref="IGrainExtension" /> 接口的生成代码使用。
        /// </summary>
        /// <typeparam name="TComponent">
        /// 要获取的组件类型。
        /// </typeparam>
        /// <param name="context">
        /// Grain 上下文。
        /// </param>
        /// <returns>
        /// Grain 扩展。
        /// </returns>
        public static TComponent GetGrainExtension<TComponent>(this IGrainContext context)
            where TComponent : class, IGrainExtension
        {
            var binder = context.GetComponent<IGrainExtensionBinder>();
            if (binder is null)
            {
                throw new InvalidOperationException($"No {nameof(IGrainExtensionBinder)} is available on the current grain context.");
            }

            return binder.GetExtension<TComponent>();
        }
    }
}
