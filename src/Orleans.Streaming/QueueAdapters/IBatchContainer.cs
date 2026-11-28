using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 每条队列消息可以包含一组类型不同且有序的事件。
    /// <see cref="IBatchContainer"/> 包含这些事件，并允许用户查询该批次中特定类型的事件。
    /// </summary>
    public interface IBatchContainer
    {
        /// <summary>
        /// 获取此批次所属流的标识符。
        /// </summary>
        StreamId StreamId { get; }

        /// <summary>
        /// 获取批次中特定类型的事件。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>();

        /// <summary>
        /// 获取此批次起始位置的流序列令牌。
        /// </summary>
        StreamSequenceToken SequenceToken { get; }

        /// <summary>
        /// 允许 <see cref="IBatchContainer"/> 在 <see cref="RequestContext"/> 中设置任意数据，然后再将此 <see cref="IBatchContainer"/> 发送给消费者。
        /// 可以是生成事件并将其加入持久化提供程序的队列时设置的数据，也可以是其他数据。
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示确实修改了 <see cref="RequestContext"/>；否则返回 <see langword="false"/>。</returns>
        bool ImportRequestContext();
    }
}
