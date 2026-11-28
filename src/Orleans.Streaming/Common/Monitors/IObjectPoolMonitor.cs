namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪对象池相关指标的监视器。
    /// </summary>
    public interface IObjectPoolMonitor
    {
        /// <summary>
        /// 每次分配对象时调用。
        /// </summary>
        void TrackObjectAllocated();

        /// <summary>
        /// 每次将对象释放并归还给池时调用。
        /// </summary>
        void TrackObjectReleased();

        /// <summary>
        /// 调用以报告对象池状态。
        /// </summary>
        /// <param name="totalObjects">对象池中的对象总数。</param>
        /// <param name="availableObjects">池中可供分配的对象数量。</param>
        /// <param name="claimedObjects">已被占用、因而不可供分配的对象数量。</param>
        void Report(long totalObjects, long availableObjects, long claimedObjects);
    }
}
