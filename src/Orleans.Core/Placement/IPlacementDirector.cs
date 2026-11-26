using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 安置主任的接口。
    /// </summary>
    public interface IPlacementDirector
    {
        /// <summary>
        /// 获取用于存储位置提示的 <see cref="PlacementTarget.RequestContextData"/> 密钥,如果存在的话。
        /// </summary>
        public const string PlacementHintKey = nameof(PlacementHintKey);

        /// <summary>
        /// 选择适当的 silo 来将指定目标锁定。
        /// </summary>
        /// <param name="strategy">目标的安置策略</param>
        /// <param name="target">所放置的 grain 以及触发该安置的请求的信息。</param>
        /// <param name="context">职位安排背景。</param>
        /// <returns>适当的 silo 将指定目标置于。</returns>
        Task<SiloAddress> OnAddActivation(
            PlacementStrategy strategy, PlacementTarget target, IPlacementContext context);

        /// <summary>
        /// 如果有且有效,则从请求中获取定位提示。
        /// </summary>
        /// <param name="requestContextData">请求的上下文数据。</param>
        /// <param name="compatibleSilos">兼容的 silos.</param>
        /// <returns>设置提示,如果是现有和有效,或 <see langword="null"/> ,否则。</returns>
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
