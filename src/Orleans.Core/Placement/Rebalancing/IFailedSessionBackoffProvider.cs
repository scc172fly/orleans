using Orleans.Internal;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 如果前一次再均衡会话失败，则确定连续再均衡会话之间的等待时间。
/// </summary>
/// <remarks>
/// 如果连续 n 个周期未能显著改善
/// 集群的熵，则认为该会话失败。
/// </remarks>
public interface IFailedSessionBackoffProvider : IBackoffProvider { }
