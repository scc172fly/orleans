namespace Orleans.Streams
{
    /// <summary>
    /// 队列适配器的常数 。
    /// </summary>
    public static class QueueAdapterConstants
    {
        /// <summary>
        /// 当 <see cref="IQueueFlowController.GetMaxAddCount"/>返回时,用于表示无限数量信件的值可以检索。
        /// </summary>
        public const int UNLIMITED_GET_QUEUE_MSG = -1;
    }
}
