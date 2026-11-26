namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪目标跟踪池相关计量
    /// </summary>
    public interface IObjectPoolMonitor
    {
        /// <summary>
        /// 每次分配对象时都呼叫 。
        /// </summary>
        void TrackObjectAllocated();

        /// <summary>
        /// 每次一个物体被放回游泳池时都呼叫
        /// </summary>
        void TrackObjectReleased();

        /// <summary>
        /// 被调来报告目标集合状态
        /// </summary>
        /// <param name="totalObjects">物体池总大小。</param>
        /// <param name="availableObjects">计算可用分配的池中对象。</param>
        /// <param name="claimedObjects">计及索赔对象,因此无法使用。</param>
        void Report(long totalObjects, long availableObjects, long claimedObjects);
    }
}
