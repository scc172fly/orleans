using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队列存储适配器。此抽象层隐藏底层排队系统的实现细节。
    /// </summary>
    public interface IQueueAdapter
    {
        /// <summary>
        /// 获取适配器的名称，主要供日志记录使用。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 将一组事件作为与指定 streamId 关联的单个批次写入队列。
        /// </summary>
        /// <typeparam name="T">队列元素的类型。</typeparam>
        /// <param name="streamId">流标识符。</param>
        /// <param name="events">事件集合。</param>
        /// <param name="token">序列令牌。</param>
        /// <param name="requestContext">请求上下文。</param>
        /// <returns>任务。</returns>
        Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext);

        /// <summary>
        /// 为指定的 queueId 创建队列接收器。
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>接收器。</returns>
        IQueueAdapterReceiver CreateReceiver(QueueId queueId);

        /// <summary>
        /// 获取一个值，指示此流适配器是否支持回溯，即是否支持从过去的时间点开始订阅。
        /// </summary>
        /// <returns>如果此流适配器支持回溯，则为 true；否则为 false。</returns>
        bool IsRewindable { get; }

        /// <summary>
        /// 获取此队列适配器的方向：<see cref="StreamProviderDirection.ReadOnly"/>、<see cref="StreamProviderDirection.WriteOnly"/> 或 <see cref="StreamProviderDirection.ReadWrite"/>。
        /// </summary>
        /// <returns>此适配器提供数据的方向。</returns>
        StreamProviderDirection Direction { get; }
    }

    /// <summary>
    /// <see cref="IQueueAdapter"/> 的扩展方法。
    /// </summary>
    public static class QueueAdapterExtensions
    {
        /// <summary>
        /// 将一组事件作为与指定 <paramref name="streamId"/> 关联的单个批次写入队列。
        /// </summary>
        /// <typeparam name="T">队列元素的类型。</typeparam>
        /// <param name="adapter">适配器。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="evt">事件。</param>
        /// <param name="token">序列令牌。</param>
        /// <param name="requestContext">请求上下文。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        public static Task QueueMessageAsync<T>(this IQueueAdapter adapter, StreamId streamId, T evt, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
        {
            return adapter.QueueMessageBatchAsync(streamId, new[] { evt }, token, requestContext);
        }
    }
}
