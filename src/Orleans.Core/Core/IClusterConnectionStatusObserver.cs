namespace Orleans;

/// <summary>
/// 接收集连接状态通知的接口。
/// </summary>
public interface IClusterConnectionStatusObserver
{
    /// <summary>
    /// 通知这位观察家,连接网关的数量已经改变。
    /// </summary>
    /// <param name="currentNumberOfGateways">
    /// 目前的网关数量 。
    /// </param>
    /// <param name="previousNumberOfGateways">
    /// 之前的网关数量 。
    /// </param>
    /// <param name="connectionRecovered">表明连接丧失问题是否已经解决。</param>
    void NotifyGatewayCountChanged(int currentNumberOfGateways, int previousNumberOfGateways, bool connectionRecovered);

    /// <summary>
    /// 通知观察员,与该组的连接已经丧失。
    /// </summary>
    void NotifyClusterConnectionLost();
}
