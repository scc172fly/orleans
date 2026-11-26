using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此界面对 IAsyncObserver 接口进行常规化,以便生产批次的物项。
    /// <para>
    /// 请注意,该接口被物品生产者(使用)所引用。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察员消费的物体类型。</typeparam>
    public interface IAsyncBatchProducer<T> : IAsyncObserver<T>
    {
        /// <summary>
        /// 将下一批物品传递给消费者。
        /// <para>
        /// 从这种方法返回的任务当批中所有物品都已填妥时,应完成
        /// 消费者经过充分加工,以满足任何行为保障。
        /// </para>
        /// <para>
        /// 也就是说,返回的任务的语义与 <see cref="IAsyncObserver{T}.OnNextAsync" />相同,
        /// 批量中的所有项目都扩展。
        /// </para>
        /// </summary>
        /// <param name="batch">有待通过的项目。</param>
        /// <param name="token">此项的流序序列符号 。</param>
        /// <returns>批次被接受后完成的任务。</returns>
        Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token = null);
    }
}
