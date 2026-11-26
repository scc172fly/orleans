namespace Orleans.Runtime;

/// <summary>
/// 一种安置战略,力求优化整个专题组的资源分配。
/// </summary>
/// <remarks>
/// <para>它为运行时统计分配加权数,以优先安排不同资源,并计算每个 silo的正常分。
/// 之后 <u>k- 选择功率</u> K 算法 silos K等于数字的平方根 silos.
/// K silos中,选择得分最低的一位来安装启动。 正常化确保每个财产按比例贡献
/// 您可以根据您在负载平衡方面的具体要求和优先级调整加权数。
/// 除了正常化外, <u>在线适应性</u> 算法还提供平滑效果(过滤高频部件)并避免快速信号
/// 将它转换成多边相似的衰变进程, 从而降低其下降。 这有利于避免在 silos 上特别是新加入的 silos上的资源饱和性 。</para>
/// <para>Silos 被界定为超负荷的卸载机制不被视为新职位的候选人。</para>
/// <para><i>此放置策略配置方法是将 <see cref="Placement.ResourceOptimizedPlacementAttribute"/> 属性添加到一个 grain。</i></para>
/// </remarks>
[GenerateSerializer, Immutable, SuppressReferenceTracking]
public sealed class ResourceOptimizedPlacement : PlacementStrategy
{
    internal static readonly ResourceOptimizedPlacement Singleton = new();
}
