namespace Orleans.Placement.Repartitioning;

/// <summary>
/// 代表控制数量之间不平衡程度的规则 grain 任何一对(可允许的) silos 正在交换激活。
/// </summary>
public interface IImbalanceToleranceRule
{
    /// <summary>
    /// 检查是否满足 <paramref name="imbalance"/>。
    /// </summary>
    /// <param name="imbalance">交换之间不平衡 silo 如果要返回此方法的话, 配对将是 <see langword="true"/></param>
    bool IsSatisfiedBy(uint imbalance);
}
