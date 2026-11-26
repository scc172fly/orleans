
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Providers
{
    /// <summary>
    /// 内模流队列的接口 grain。
    /// </summary>
    public interface IMemoryStreamQueueGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 加入一个事件。
        /// </summary>
        /// <param name="data">数据。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("Enqueue")]
        Task Enqueue(MemoryMessageData data);

        /// <summary>
        /// 加入一个事件。
        /// </summary>
        /// <param name="data">数据。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("74D60341")]
        Task Enqueue(MemoryMessageData data, CancellationToken cancellationToken) => Enqueue(data);

        /// <summary>
        /// 取消最多为 <paramref name="maxCount"/> 事件。
        /// </summary>
        /// <param name="maxCount">
        /// 最大取消事件次数 。
        /// </param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("Dequeue")]
        Task<List<MemoryMessageData>> Dequeue(int maxCount);

        /// <summary>
        /// 取消最多为 <paramref name="maxCount"/> 事件。
        /// </summary>
        /// <param name="maxCount">取消排队的最大事件次数。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("7A8F8C1A")]
        Task<List<MemoryMessageData>> Dequeue(int maxCount, CancellationToken cancellationToken) => Dequeue(maxCount);
    }
}
