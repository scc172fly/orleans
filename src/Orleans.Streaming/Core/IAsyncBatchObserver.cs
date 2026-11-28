using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 表示序列中的一个流数据项。
    /// </summary>
    /// <typeparam name="T">数据项的类型。</typeparam>
    [GenerateSerializer]
    public class SequentialItem<T>
    {
        /// <summary>
        /// 初始化 <see cref="SequentialItem{T}"/> 类的新实例。
        /// </summary>
        /// <param name="item">数据项。</param>
        /// <param name="token">序列令牌。</param>
        public SequentialItem(T item, StreamSequenceToken token)
        {
            this.Item = item;
            this.Token = token;
        }

        /// <summary>
        /// 获取数据项。
        /// </summary>
        /// <value>数据项。</value>
        [Id(0)]
        public T Item { get; }

        /// <summary>
        /// 获取令牌。
        /// </summary>
        /// <value>序列令牌。</value>
        [Id(1)]
        public StreamSequenceToken Token { get; }
    }

    /// <summary>
    /// 此接口扩展 IAsyncObserver 接口，使生产者和消费者能够批量处理数据项。
    /// <para>
    /// 注意，此接口由数据项的消费者实现，由生产者调用。
    /// 因此，流的消费端实现此接口。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察者消费的对象类型。</typeparam>
    public interface IAsyncBatchObserver<T>
    {
        /// <summary>
        /// 将下一批数据项传递给消费者。
        /// <para>
        /// 当消费者对这些数据项的处理达到
        /// 满足各项行为保证所需的程度时，此方法返回的 Task 才应完成。
        /// </para>
        /// <para>
        /// 当消费者是流（的生产端）时，Task 在流实现
        /// 已接管这些数据项并确保能够满足其投递保证后完成。
        /// 例如，基于持久化队列的流会在这些数据项已持久保存后完成 Task。
        /// 提供尽力而为、至多一次投递的流会返回一个已经完成的 Task。
        /// </para>
        /// <para>
        /// 当生产者是流（的消费端）时，消费者代码应在
        /// 已接管这些数据项后完成 Task。
        /// 特别是，如果流提供程序保证至少一次投递，则只有在
        /// 消费者返回的 Task 完成后，才能认为这些数据项已投递。
        /// </para>
        /// </summary>
        /// <param name="items">要传递的数据项。</param>
        /// <returns>数据项被接受时完成的 Task。</returns>
        Task OnNextAsync(IList<SequentialItem<T>> items);

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
