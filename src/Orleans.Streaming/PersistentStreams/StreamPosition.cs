
using System;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流位置为在流中指定事件的位置。
    /// 如果为一系列事件获取流位置, 则流位置将是批次中的第一个事件 。
    /// </summary>
    public class StreamPosition
    {
        /// <summary>
        /// 流流位置由流流身份和序列符号组成
        /// </summary>
        /// <param name="streamId">流体身份。</param>
        /// <param name="sequenceToken">流序序列符号。</param>
        public StreamPosition(StreamId streamId, StreamSequenceToken sequenceToken)
        {
            if (sequenceToken == null)
            {
                throw new ArgumentNullException(nameof(sequenceToken));
            }
            StreamId = streamId;
            SequenceToken = sequenceToken;
        }

        /// <summary>
        /// 获取流的特性
        /// </summary>
        public StreamId StreamId { get; }

        /// <summary>
        /// 获得流中的位置
        /// </summary>
        public StreamSequenceToken SequenceToken { get; }
    }
}
