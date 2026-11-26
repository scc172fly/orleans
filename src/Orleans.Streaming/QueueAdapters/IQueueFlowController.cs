

namespace Orleans.Streams
{
    /// <summary>
    /// 控制检索到的队列项目流动的职能。
    /// </summary>
    public interface IQueueFlowController
    {
        /// <summary>
        /// 获取可以添加的最大项目数。
        /// </summary>
        /// <returns>
        /// 可添加的项目的最大数量 。
        /// </returns>
        int GetMaxAddCount();
    }
}
