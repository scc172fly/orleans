using System.Diagnostics.CodeAnalysis;
using Orleans.Streams;

namespace TestGrains
{
    [Serializable]
    [Orleans.GenerateSerializer]
    public class StreamCheckpoint<TState>
    {
        [Orleans.Id(0)]
        public Guid StreamGuid { get; set; }
        [Orleans.Id(1)]
        public string? StreamNamespace { get; set; }
        [Orleans.Id(2)]
        public StreamSequenceToken? StartToken { get; set; }
        [Orleans.Id(3)]
        public StreamSequenceToken? LastProcessedToken { get; set; }
        [Orleans.Id(4)]
        [MaybeNull]
        public TState Accumulator { get; set; }

        public StreamSequenceToken? RecoveryToken { get { return LastProcessedToken ?? StartToken; } }

        public bool IsDuplicate(StreamSequenceToken sequenceToken)
        {
            // 这是第一次,所以不能是重复
            if (StartToken == null)
                return false;

            // 如果我们处理过事件 和最后事件的顺序符号相比
            if (LastProcessedToken != null)
            {
                // 如果上次处理的最后一个进程不比此序列顺序的旧, 那么这个符号是一个重复
                return !LastProcessedToken.Older(sequenceToken);
            }

            // 如果我们只有开始的象征, 那么我们还没有处理第一个事件, 所以我们应该处理任何 开始的象征或之后的事件。
            return StartToken.Newer(sequenceToken);
        }

        public bool TryUpdateStartToken(StreamSequenceToken sequenceToken)
        {
            if (StartToken == null)
            {
                StartToken = sequenceToken;
                return true;
            }
            return false;
        }
    }
}
