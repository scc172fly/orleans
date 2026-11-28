
using System;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流位置唯一标识事件在流中的位置。
    /// 如果获取一批事件的流位置，则返回批次中第一个事件的位置。
    /// </summary>
    public class StreamPosition
    {
        /// <summary>
        /// 流位置由流标识和序列令牌组成。
        /// </summary>
        /// <param name="streamId">流标识。</param>
        /// <param name="sequenceToken">流序列令牌。</param>
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
        /// 获取流的标识。
        /// </summary>
        public StreamId StreamId { get; }

        /// <summary>
        /// 获取在流中的位置。
        /// </summary>
        public StreamSequenceToken SequenceToken { get; }
    }
}
