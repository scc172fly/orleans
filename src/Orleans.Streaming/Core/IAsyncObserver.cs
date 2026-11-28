using System;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此接口扩展标准 .NET IObserver 接口，支持异步生产数据项。
    /// <para>
    /// 注意，此接口由数据项的消费者实现，由生产者调用。
    /// 因此，流的消费端实现此接口。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察者消费的对象类型。</typeparam>
    public interface IAsyncObserver<in T>
    {
        /// <summary>
        /// 将下一个数据项传递给消费者。
        /// <para>
        /// 当消费者对此数据项的处理达到
        /// 满足各项行为保证所需的程度时，此方法返回的 Task 才应完成。
        /// </para>
        /// <para>
        /// 当消费者是流（的生产端）时，Task 在流实现
        /// 已接管该数据项并确保能够满足其投递保证后完成。
        /// 例如，基于持久化队列的流会在该数据项已持久保存后完成 Task。
        /// 提供尽力而为、至多一次投递的流会返回一个已经完成的 Task。
        /// </para>
        /// <para>
        /// 当生产者是流（的消费端）时，消费者代码应在
        /// 已接管该数据项后完成 Task。
        /// 特别是，如果流提供程序保证至少一次投递，则只有在
        /// 消费者返回的 Task 完成后，才能认为该数据项已投递。
        /// </para>
        /// </summary>
        /// <param name="item">要传递的数据项。</param>
        /// <param name="token">此数据项的流序列令牌。</param>
        /// <returns>数据项被接受时完成的 Task。</returns>
        Task OnNextAsync(T item, StreamSequenceToken? token = null);

        /// <summary>
        /// 通知消费者流已完成。
        /// <para>
        /// 当消费者完成流关闭处理时，此方法返回的 Task 应当完成。
        /// </para>
        /// </summary>
        /// <returns>流完成操作被接受时完成的 Task。</returns>
        Task OnCompletedAsync() => Task.CompletedTask;

        /// <summary>
        /// 通知消费者流发生错误。
        /// <para>
        /// 当消费者完成流关闭处理时，此方法返回的 Task 应当完成。
        /// </para>
        /// </summary>
        /// <param name="ex">描述流中所发生错误的 Exception。</param>
        /// <returns>关闭操作被接受时完成的 Task。</returns>
        Task OnErrorAsync(Exception ex);
    }
}
