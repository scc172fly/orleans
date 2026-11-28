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
            // 这是第一个事件，因此不可能是重复事件
            if (StartToken == null)
                return false;

            // 如果已经处理过事件，则与上次处理事件的序列令牌比较。
            if (LastProcessedToken != null)
            {
                // 如果最后处理的序列令牌不早于当前序列令牌，则当前令牌是重复的
                return !LastProcessedToken.Older(sequenceToken);
            }

            // 如果仅保存了起始令牌，则尚未处理第一个事件，应处理起始令牌及其后的所有事件。
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
