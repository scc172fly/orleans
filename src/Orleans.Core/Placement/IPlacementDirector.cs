using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 放置决策器的接口。
    /// </summary>
    public interface IPlacementDirector
    {
        /// <summary>
        /// 获取用于存储放置提示（若存在）的 <see cref="PlacementTarget.RequestContextData"/> 键。
        /// </summary>
        public const string PlacementHintKey = nameof(PlacementHintKey);

        /// <summary>
        /// 选择合适的 Silo 来放置指定目标。
        /// </summary>
        /// <param name="strategy">目标的放置策略。</param>
        /// <param name="target">正在放置的 Grain，以及触发放置的请求的信息。</param>
        /// <param name="context">放置上下文。</param>
        /// <returns>适合放置指定目标的 Silo。</returns>
        Task<SiloAddress> OnAddActivation(
            PlacementStrategy strategy, PlacementTarget target, IPlacementContext context);

        /// <summary>
        /// 从提供的请求上下文数据中获取放置提示（如果存在且有效）。
        /// </summary>
        /// <param name="requestContextData">请求上下文数据。</param>
        /// <param name="compatibleSilos">兼容的 Silo。</param>
        /// <returns>放置提示（如果存在且有效），否则为 <see langword="null"/>。</returns>
        public static SiloAddress? GetPlacementHint(Dictionary<string, object> requestContextData, SiloAddress[] compatibleSilos)
        {
            if (requestContextData is { Count: > 0 } data
                && data.TryGetValue(PlacementHintKey, out var value)
                && value is SiloAddress placementHint
                && compatibleSilos.Contains(placementHint))
            {
                return placementHint;
            }

            return null;
        }
    }
}
