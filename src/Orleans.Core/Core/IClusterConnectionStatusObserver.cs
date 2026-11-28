namespace Orleans;

/// <summary>
/// 接收集群连接状态通知的接口。
/// </summary>
public interface IClusterConnectionStatusObserver
{
    /// <summary>
    /// 通知此观察者已连接的网关数量发生变化。
    /// </summary>
    /// <param name="currentNumberOfGateways">
    /// 当前网关数量。
    /// </param>
    /// <param name="previousNumberOfGateways">
    /// 此前的网关数量。
    /// </param>
    /// <param name="connectionRecovered">指示丢失的连接是否已恢复。</param>
    void NotifyGatewayCountChanged(int currentNumberOfGateways, int previousNumberOfGateways, bool connectionRecovered);

    /// <summary>
    /// 通知此观察者与集群的连接已丢失。
    /// </summary>
    void NotifyClusterConnectionLost();
}
