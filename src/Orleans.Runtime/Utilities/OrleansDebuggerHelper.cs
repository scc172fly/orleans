using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Utilities
{
    /// <summary>
    /// 协助调试器会议的有效方法。
    /// </summary>
    public static class OrleansDebuggerHelper
    {
        /// <summary>
        /// 返回 grain 与所提供的对应的事例 <paramref name="grainReference"/> 如果当前时激活 silo.
        /// </summary>
        /// <param name="grainReference">下 grain 参考。</param>
        /// <returns>
        /// grain 实例与所提供的 <paramref name="grainReference"/> 对应,如果在目前 silo或 <see langword="null"/> 时启动。
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
