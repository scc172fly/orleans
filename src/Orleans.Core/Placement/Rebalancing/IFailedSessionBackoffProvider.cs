using Orleans.Internal;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 确定如果前一届会议失败,在相继的重新平衡会议之间要等多久。
/// </summary>
/// <remarks>
/// 如果周期的连续周期数目没有显著改善,则将视为“失败”一课
/// 组群的星体
/// </remarks>
public interface IFailedSessionBackoffProvider : IBackoffProvider { }
