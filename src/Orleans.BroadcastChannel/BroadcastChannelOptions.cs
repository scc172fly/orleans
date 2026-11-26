namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 广播频道配置选项
    /// </summary>
    public class BroadcastChannelOptions
    {
        /// <summary>
        /// 如果设定为真实, 服务提供方不会等待订阅者的呼叫 。
        /// </summary>
        public bool FireAndForgetDelivery { get; set; } = true;
    }
}

