namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Orleans ServiceBus 错误代码。
    /// </summary>
    internal enum OrleansEventHubErrorCode
    {
        /// <summary>
        /// Orleans ServiceBus 错误代码的起始位置。
        /// </summary>
        ServiceBus = 1 << 16,

        FailedPartitionRead = ServiceBus + 1,
        RetryReceiverInit = ServiceBus + 2,
    }
}
