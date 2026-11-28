namespace Orleans.Runtime;

/// <summary>
/// 一种尝试优化集群资源分布的放置策略。
/// </summary>
/// <remarks>
/// <para>它为运行时统计数据分配权重，以区分不同资源的优先级，并计算每个 Silo 的归一化评分。
/// 根据 <u>K 次选择之力（power of k-choices）</u> 算法，选取 K 个 Silo 作为潜在目标，其中 K 等于 Silo 数量的平方根。
/// 在这 K 个 Silo 中，选择评分最低的 Silo 来放置激活实例。归一化保证每项属性按比例
/// 对总体评分作出贡献。可以根据负载均衡的具体要求和优先级调整权重。
/// 除归一化之外，<u>在线自适应</u> 算法提供平滑效果（滤除高频成分），并通过将信号的快速下降
/// 转换为类似多项式的衰减过程来避免这种骤降。这有助于防止 Silo（尤其是新加入的 Silo）出现资源饱和。</para>
/// <para>根据负载削减机制的定义已经过载的 Silo，不会被视为新放置的候选目标。</para>
/// <para><i>通过向 Grain 添加 <see cref="Placement.ResourceOptimizedPlacementAttribute"/> 特性来配置此放置策略。</i></para>
/// </remarks>
[GenerateSerializer, Immutable, SuppressReferenceTracking]
public sealed class ResourceOptimizedPlacement : PlacementStrategy
{
    internal static readonly ResourceOptimizedPlacement Singleton = new();
}
