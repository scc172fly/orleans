using System;

namespace Orleans
{
    /// <summary>
    /// 客户端断开集的处理器
    /// </summary>
    /// <param name="sender">发件人</param>
    /// <param name="e">事件参数。</param>
    public delegate void ConnectionToClusterLostHandler(object? sender, EventArgs e);

    /// <summary>
    /// 网关数目的处理器
    /// </summary>
    /// <param name="sender">发件人</param>
    /// <param name="e">事件参数。</param>
    public delegate void GatewayCountChangedHandler(object? sender, GatewayCountChangedEventArgs e);

    /// <summary>
    /// 网关连接活动的活动论证。
    /// </summary>
    public class GatewayCountChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 获取此客户端当前连接的网关数 。
        /// </summary>
        public int NumberOfConnectedGateways { get; }

        /// <summary>
        /// 获取此客户端在此事件之前连接到的网关数 。
        /// </summary>
        public int PreviousNumberOfConnectedGateways { get; }

        /// <summary>
        /// 帮助发现集群连通恢复的情况。
        /// </summary>
        public bool ConnectionRecovered => this.NumberOfConnectedGateways > 0 && this.PreviousNumberOfConnectedGateways <= 0;

        /// <summary>
        /// 初始化 <see cref="GatewayCountChangedEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="currentNumberOfConnectedGateways">
        /// 连接网关的当前数量 。
        /// </param>
        /// <param name="previousNumberOfConnectedGateways">
        /// 之前连接的网关数量 。
        /// </param>
        public GatewayCountChangedEventArgs(int currentNumberOfConnectedGateways, int previousNumberOfConnectedGateways)
        {
            this.NumberOfConnectedGateways = currentNumberOfConnectedGateways;
            this.PreviousNumberOfConnectedGateways = previousNumberOfConnectedGateways;
        }
    }
}
