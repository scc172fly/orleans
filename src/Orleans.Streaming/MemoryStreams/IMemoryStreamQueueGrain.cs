
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Providers
{
    /// <summary>
    /// 内存流队列 Grain 的接口。
    /// </summary>
    public interface IMemoryStreamQueueGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将一个事件加入队列。
        /// </summary>
        /// <param name="data">数据。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("Enqueue")]
        Task Enqueue(MemoryMessageData data);

        /// <summary>
        /// 将一个事件加入队列。
        /// </summary>
        /// <param name="data">数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("74D60341")]
        Task Enqueue(MemoryMessageData data, CancellationToken cancellationToken) => Enqueue(data);

        /// <summary>
        /// 从队列中取出最多 <paramref name="maxCount"/> 个事件。
        /// </summary>
        /// <param name="maxCount">
        /// 最多出队的事件数量。
        /// </param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("Dequeue")]
        Task<List<MemoryMessageData>> Dequeue(int maxCount);

        /// <summary>
        /// 从队列中取出最多 <paramref name="maxCount"/> 个事件。
        /// </summary>
        /// <param name="maxCount">最多出队的事件数量。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("7A8F8C1A")]
        Task<List<MemoryMessageData>> Dequeue(int maxCount, CancellationToken cancellationToken) => Dequeue(maxCount);
    }
}
