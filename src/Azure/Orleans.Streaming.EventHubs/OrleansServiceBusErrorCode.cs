namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Orleans ServiceBus 错误代码
    /// </summary>
    internal enum OrleansEventHubErrorCode
    {
        /// <summary>
        /// 开始 Olean 服务客车错误代码
        /// </summary>
        ServiceBus = 1 << 16,

        FailedPartitionRead = ServiceBus + 1,
        RetryReceiverInit = ServiceBus + 2,
    }
}
