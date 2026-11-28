using Orleans.Metadata;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 将 <see cref="PlacementStrategy"/> 与 <see cref="GrainType"/> 关联。
    /// </summary>
    public interface IPlacementStrategyResolver
    {
        /// <summary>
        /// 获取与指定 Grain 类型关联的放置策略。
        /// </summary>
        bool TryResolvePlacementStrategy(GrainType grainType, GrainProperties? properties, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PlacementStrategy result);
    }
}
