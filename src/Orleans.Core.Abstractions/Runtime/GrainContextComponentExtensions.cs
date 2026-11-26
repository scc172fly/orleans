using System;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 延期 <see cref="IGrainContext"/> 与 <see cref="IGrainExtension"/>.
    /// </summary>
    public static class GrainContextComponentExtensions
    {
        /// <summary>
        /// 被生成的 <see cref="IGrainExtension" /> 接口代码所使用。
        /// </summary>
        /// <typeparam name="TComponent">
        /// 要获取的组件类型 。
        /// </typeparam>
        /// <param name="context">
        /// grain 上下文。
        /// </param>
        /// <returns>
        /// 延长 grain 。
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
