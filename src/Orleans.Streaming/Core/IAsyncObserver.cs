using System;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 此界面对标准 .NET Iserve 接口进行通用化,允许不同步生产物品。
    /// <para>
    /// 请注意,该接口由物品消费者实施,并由物品生产者援引(使用)。
    /// 这意味着流的消费端点执行此接口 。
    /// </para>
    /// </summary>
    /// <typeparam name="T">观察员消费的物体类型。</typeparam>
    public interface IAsyncObserver<in T>
    {
        /// <summary>
        /// 将下一个项目传递给消费者。
        /// <para>
        /// 从此方法返回的任务处理完成后,项目处理完成后即应完成
        /// 消费者经过充分加工,以满足任何行为保障。
        /// </para>
        /// <para>
        /// 当消费者是流(生产端点)时,当流执行时任务即告完成
        /// 已经接受对物品的责任,并保证能够履行交付保证。
        /// 例如,以耐久队列为基础的流将完成任务,当项目被持久保存时。
        /// 一条流,最多能提供最佳努力的交付一次,将返回已经完成的任务。
        /// </para>
        /// <para>
        /// 当生产者是(消费端点)流流时,任务应当由消费者代码完成
        /// 接受对该项目的责任时。 
        /// 特别是,如果流流提供商保证在交货后至少一次交付时即予以担保,则不应审议该物品
        /// 直至消费者交还的任务完成为止。
        /// </para>
        /// </summary>
        /// <param name="item">待通过的项目。</param>
        /// <param name="token">此项的流序序列符号 。</param>
        /// <returns>一项在项目被接受后完成的任务。</returns>
        Task OnNextAsync(T item, StreamSequenceToken? token = null);

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
