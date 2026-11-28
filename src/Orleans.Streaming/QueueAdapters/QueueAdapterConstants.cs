namespace Orleans.Streams
{
    /// <summary>
    /// 队列适配器的常量。
    /// </summary>
    public static class QueueAdapterConstants
    {
        /// <summary>
        /// 当 <see cref="IQueueFlowController.GetMaxAddCount"/> 返回此值时，表示可以获取不限数量的消息。
        /// </summary>
        public const int UNLIMITED_GET_QUEUE_MSG = -1;
    }
}
