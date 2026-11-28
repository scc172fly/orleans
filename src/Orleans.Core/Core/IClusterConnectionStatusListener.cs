namespace Orleans
{
    /// <summary>
    /// 用于通知观察者集群连接已丢失的接口。
    /// </summary>
    internal interface IClusterConnectionStatusListener
    {
        /// <summary>
        /// 通知此客户端已连接的网关数量发生变化。
        /// </summary>
        /// <param name="currentNumberOfGateways">
        /// 当前网关数量。
        /// </param>
        /// <param name="previousNumberOfGateways">
        /// 此前的网关数量。
        /// </param>
        void NotifyGatewayCountChanged(int currentNumberOfGateways, int previousNumberOfGateways);

        /// <summary>
        /// 通知此客户端与集群的连接已丢失。
        /// </summary>
        void NotifyClusterConnectionLost();
    }
}
