

namespace Orleans.Streams
{
    /// <summary>
    /// 提供控制所获取队列数据项流量的功能。
    /// </summary>
    public interface IQueueFlowController
    {
        /// <summary>
        /// 获取可添加的最大数据项数量。
        /// </summary>
        /// <returns>
        /// 可添加的最大数据项数量。
        /// </returns>
        int GetMaxAddCount();
    }
}
