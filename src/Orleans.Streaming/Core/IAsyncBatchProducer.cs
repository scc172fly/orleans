using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此接口扩展 IAsyncObserver 接口，支持批量生产数据项。
    /// <para>
    /// 注意，此接口由数据项的生产者调用。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察者消费的对象类型。</typeparam>
    public interface IAsyncBatchProducer<T> : IAsyncObserver<T>
    {
        /// <summary>
        /// 将下一批数据项传递给消费者。
        /// <para>
        /// 当消费者对批次中所有数据项的处理达到
        /// 满足各项行为保证所需的程度时，此方法返回的 Task 才应完成。
        /// </para>
        /// <para>
        /// 即返回的 Task 具有与 <see cref="IAsyncObserver{T}.OnNextAsync" /> 相同的语义，
        /// 但作用范围扩展到批次中的所有数据项。
        /// </para>
        /// </summary>
        /// <param name="batch">要传递的数据项集合。</param>
        /// <param name="token">此数据项的流序列令牌。</param>
        /// <returns>批次被接受时完成的 Task。</returns>
        Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token = null);
    }
}
