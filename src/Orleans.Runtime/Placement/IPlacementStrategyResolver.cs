using Orleans.Metadata;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// a <see cref="PlacementStrategy"/> 与 <see cref="GrainType"/>有关。
    /// </summary>
    public interface IPlacementStrategyResolver
    {
        /// <summary>
        /// 获得与提供 grain 类型相关的安置策略。
        /// </summary>
        bool TryResolvePlacementStrategy(GrainType grainType, GrainProperties? properties, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PlacementStrategy result);
    }
}
