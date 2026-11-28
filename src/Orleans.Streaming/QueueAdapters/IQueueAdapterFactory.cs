using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 适配器工厂，应根据流提供程序配置创建适配器。
    /// </summary>
    public interface IQueueAdapterFactory
    {
        /// <summary>
        /// 创建队列适配器。
        /// </summary>
        /// <returns>队列适配器。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IQueueAdapter> CreateAdapter();

        /// <summary>
        /// 创建队列适配器。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>队列适配器。</returns>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        Task<IQueueAdapter> CreateAdapter(CancellationToken cancellationToken) => CreateAdapter();
#pragma warning restore CS0618

        /// <summary>
        /// 创建队列消息缓存适配器。
        /// </summary>
        /// <returns>队列适配器缓存。</returns>
        IQueueAdapterCache GetQueueAdapterCache();

        /// <summary>
        /// 创建队列映射器。
        /// </summary>
        /// <returns>队列映射器。</returns>
        IStreamQueueMapper GetStreamQueueMapper();

        /// <summary>
        /// 获取队列的投递失败处理程序。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>流故障处理程序。</returns>
        Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId);
    }
}
