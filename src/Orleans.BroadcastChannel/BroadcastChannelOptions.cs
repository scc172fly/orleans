namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 广播通道的配置选项。
    /// </summary>
    public class BroadcastChannelOptions
    {
        /// <summary>
        /// 如果设置为 true，则提供程序不会等待对订阅者的调用完成。
        /// </summary>
        public bool FireAndForgetDelivery { get; set; } = true;
    }
}

