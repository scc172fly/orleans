using System;

namespace Orleans
{
    /// <summary>
    /// 客户端与集群断开连接的处理程序。
    /// </summary>
    /// <param name="sender">发送方。</param>
    /// <param name="e">事件参数。</param>
    public delegate void ConnectionToClusterLostHandler(object? sender, EventArgs e);

    /// <summary>
    /// 网关数量变更的处理程序。
    /// </summary>
    /// <param name="sender">发送方。</param>
    /// <param name="e">事件参数。</param>
    public delegate void GatewayCountChangedHandler(object? sender, GatewayCountChangedEventArgs e);

    /// <summary>
    /// 网关连接事件的事件参数。
    /// </summary>
    public class GatewayCountChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 获取此客户端当前连接的网关数量。
        /// </summary>
        public int NumberOfConnectedGateways { get; }

        /// <summary>
        /// 获取此事件发生之前客户端连接的网关数量。
        /// </summary>
        public int PreviousNumberOfConnectedGateways { get; }

        /// <summary>
        /// 用于检测集群连接恢复情况的辅助属性。
        /// </summary>
        public bool ConnectionRecovered => this.NumberOfConnectedGateways > 0 && this.PreviousNumberOfConnectedGateways <= 0;

        /// <summary>
        /// 初始化 <see cref="GatewayCountChangedEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="currentNumberOfConnectedGateways">
        /// 当前已连接的网关数量。
        /// </param>
        /// <param name="previousNumberOfConnectedGateways">
        /// 此前已连接的网关数量。
        /// </param>
        public GatewayCountChangedEventArgs(int currentNumberOfConnectedGateways, int previousNumberOfConnectedGateways)
        {
            this.NumberOfConnectedGateways = currentNumberOfConnectedGateways;
            this.PreviousNumberOfConnectedGateways = previousNumberOfConnectedGateways;
        }
    }
}
