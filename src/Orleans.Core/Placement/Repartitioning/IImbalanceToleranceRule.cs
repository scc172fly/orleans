namespace Orleans.Placement.Repartitioning;

/// <summary>
/// 表示一条规则，用于控制任意两个 Silo 交换激活实例时，可容忍的 Grain 激活实例数量不均衡程度。
/// </summary>
public interface IImbalanceToleranceRule
{
    /// <summary>
    /// 检查 <paramref name="imbalance"/> 是否满足此规则。
    /// </summary>
    /// <param name="imbalance">如果此方法返回 <see langword="true"/>，则两个交换方 Silo 之间将出现的预期不均衡程度。</param>
    bool IsSatisfiedBy(uint imbalance);
}
