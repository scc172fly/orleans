using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Utilities
{
    /// <summary>
    /// 辅助调试会话的实用方法。
    /// </summary>
    public static class OrleansDebuggerHelper
    {
        /// <summary>
        /// 如果提供的 <paramref name="grainReference"/> 对应的 Grain 在当前 Silo 上已激活，则返回 Grain 实例。
        /// </summary>
        /// <param name="grainReference">Grain 引用。</param>
        /// <returns>
        /// 提供的 <paramref name="grainReference"/> 对应的 Grain 实例（如果已在当前 Silo 上激活），否则返回 <see langword="null"/>。
        /// </returns>
        public static object? GetGrainInstance(object? grainReference)
        {
            switch (grainReference)
            {
                case Grain:
                case IGrainBase:
                case ISystemTargetBase:
                    return grainReference;
                case GrainReference reference:
                    {
                        var runtime = (reference.Runtime as GrainReferenceRuntime)?.RuntimeClient;
                        var activations = runtime?.ServiceProvider.GetService<ActivationDirectory>();
                        var grains = activations?.FindTarget(reference.GrainId);
                        return grains?.GrainInstance;
                    }
                default:
                    return null;
            }
        }
    }
}
