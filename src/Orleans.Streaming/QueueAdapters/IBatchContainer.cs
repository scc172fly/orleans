using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 允许每条队列信息有不同、有顺序的一组事件。
    /// <see cref="IBatchContainer"/> 包含这些活动,用户可以查询特定类型事件的批次。
    /// </summary>
    public interface IBatchContainer
    {
        /// <summary>
        /// Ges 用于此批次所包含串流的溪流识别符 。
        /// </summary>
        StreamId StreamId { get; }

        /// <summary>
        /// 从批量中获取特定类型的事件 。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>();

        /// <summary>
        /// 用于开始此批次的串流序列符号 。
        /// </summary>
        StreamSequenceToken SequenceToken { get; }

        /// <summary>
        /// 给一个机会 <see cref="IBatchContainer"/> 中设置数据 <see cref="RequestContext"/> 此前 <see cref="IBatchContainer"/> 发送到消费者。
        /// 可以是生成事件时设定的数据,并排入持续提供方或任何其他数据。
        /// </summary>
        /// <returns><see langword="true"/> 如果 <see cref="RequestContext"/> 确实被修改,则 <see langword="false"/> 。</returns>
        bool ImportRequestContext();
    }
}
