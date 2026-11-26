using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 代表一个序列中的流项目。
    /// </summary>
    /// <typeparam name="T">物品类型。</typeparam>
    [GenerateSerializer]
    public class SequentialItem<T>
    {
        /// <summary>
        /// 初始化 <see cref="SequentialItem{T}"/> 类的新实例。
        /// </summary>
        /// <param name="item">这个项目</param>
        /// <param name="token">牌子。</param>
        public SequentialItem(T item, StreamSequenceToken token)
        {
            this.Item = item;
            this.Token = token;
        }

        /// <summary>
        /// 得到的东西。
        /// </summary>
        /// <value>这个项目</value>
        [Id(0)]
        public T Item { get; }

        /// <summary>
        /// 获得标志。
        /// </summary>
        /// <value>牌子。</value>
        [Id(1)]
        public StreamSequenceToken Token { get; }
    }

    /// <summary>
    /// 这个界面对 IAsyncObserver 接口进行统称,允许生产批次的物品和消费。
    /// <para>
    /// 请注意,该接口由物品消费者实施,并由物品生产者援引(使用)。
    /// 这意味着流的消费端点执行此接口 。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察员消费的物体类型。</typeparam>
    public interface IAsyncBatchObserver<T>
    {
        /// <summary>
        /// 将下一批物品传递给消费者。
        /// <para>
        /// 从这种方法返回的任务应在项目处理完成后完成
        /// 消费者经过充分加工,以满足任何行为保障。
        /// </para>
        /// <para>
        /// 当消费者是流(生产端点)时,当流执行时任务即告完成
        /// 已经接受对物品的责任,并保证能够履行交付保证。
        /// 例如,在项目保存得持久时,以耐久队列为基础的流将完成任务。
        /// 一条流,最多能提供最佳努力的交付一次,将返回已经完成的任务。
        /// </para>
        /// <para>
        /// 当生产者是(消费端点)流流时,任务应当由消费者代码完成
        /// 接受对这些项目的责任。 
        /// 特别是,如果流流提供商保证在货物交付后至少交货时即予以担保,则不应审议这些物项
        /// 直至消费者交还的任务完成为止。
        /// </para>
        /// </summary>
        /// <param name="items">待通过的项目。</param>
        /// <returns>一项在项目被接受后完成的任务。</returns>
        Task OnNextAsync(IList<SequentialItem<T>> items);

        /// <summary>
        /// 通知消费者该河流已经完成。
        /// <para>
        /// 从这种方法返回的任务应在消费者处理完流关闭后完成。
        /// </para>
        /// </summary>
        /// <returns>任务在接受流完成作业后完成。</returns>
        Task OnCompletedAsync() => Task.CompletedTask;

        /// <summary>
        /// 通知消费者流出了错误
        /// <para>
        /// 从这种方法返回的任务应在消费者处理完流关闭后完成。
        /// </para>
        /// </summary>
        /// <param name="ex">一个描述流上发生错误的例外。</param>
        /// <returns>一项在接受关闭时完成的任务。</returns>
        Task OnErrorAsync(Exception ex);
    }
}
