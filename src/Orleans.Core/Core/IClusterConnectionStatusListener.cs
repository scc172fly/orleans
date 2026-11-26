namespace Orleans
{
    /// <summary>
    /// 通知观察员该组群连接已丢失的接口。
    /// </summary>
    internal interface IClusterConnectionStatusListener
    {
        /// <summary>
        /// 通知客户端, 已更改连接网关数量
        /// </summary>
        /// <param name="currentNumberOfGateways">
        /// 目前的网关数量 。
        /// </param>
        /// <param name="previousNumberOfGateways">
        /// 之前的网关数量 。
        /// </param>
        void NotifyGatewayCountChanged(int currentNumberOfGateways, int previousNumberOfGateways);

        /// <summary>
        /// 通知客户该组群已失去连接。
        /// </summary>
        void NotifyClusterConnectionLost();
    }
}
