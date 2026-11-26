using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 适配器工厂。 这应该从流提供商配置中创建一个适配器
    /// </summary>
    public interface IQueueAdapterFactory
    {
        /// <summary>
        /// 创建队列适配器。
        /// </summary>
        /// <returns>队列适配器</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IQueueAdapter> CreateAdapter();

        /// <summary>
        /// 创建队列适配器。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>队列适配器。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task<IQueueAdapter> CreateAdapter(CancellationToken cancellationToken) => CreateAdapter();
#pragma warning restore CS0618

        /// <summary>
        /// 创建队列信件缓存适配器。
        /// </summary>
        /// <returns>队列适配器缓存。</returns>
        IQueueAdapterCache GetQueueAdapterCache();

        /// <summary>
        /// 创建队列映射仪。
        /// </summary>
        /// <returns>队列映像仪。</returns>
        IStreamQueueMapper GetStreamQueueMapper();

        /// <summary>
        /// 获取队列处理器的交付失败处理器
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>流流处理器故障。</returns>
        Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId);
    }
}
