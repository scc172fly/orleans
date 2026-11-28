using System;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.Streams
{
    [Serializable]
    internal enum StreamConsumerDataState
    {
        Active, // 表示正在主动向此消费者投递事件。
        Inactive, // 表示当前未主动向此消费者投递事件。如果适配器在该消费者的流上产生事件，代理将需要开始投递事件。
    }

    [Serializable]
    [GenerateSerializer]
    internal sealed class StreamConsumerData
    {
        [Id(0)]
        public GuidId SubscriptionId;
        [Id(1)]
        public QualifiedStreamId StreamId;
        [Id(2)]
        public IStreamConsumerExtension StreamConsumer;
        [Id(3)]
        public StreamConsumerDataState State = StreamConsumerDataState.Inactive;
        [Id(4)]
        public IQueueCacheCursor? Cursor;
        [Id(5)]
        public StreamHandshakeToken? LastToken;
        [Id(6)]
        public string? FilterData;

        [NonSerialized]
        public bool IsRegistered = false;
        [NonSerialized]
        public StreamSequenceToken? PendingStartToken;
        [NonSerialized]
        public IBatchContainer? PendingBatch;
        [NonSerialized]
        public bool StartPositionIsProviderDefault;

        /// <summary>
        /// 此订阅最后处理（已投递或已筛除）的批次的序列令牌。
        /// 拉取代理的周期性扫描使用此令牌计算基于投递进度的检查点水位线。
        /// </summary>
        [NonSerialized]
        public StreamSequenceToken? LastProcessedToken;

        [NonSerialized]
        public int PendingHandshakes;
        [NonSerialized]
        public long HandshakeGeneration;
        [NonSerialized]
        public bool HasUnresolvedHandshake;
        [NonSerialized]
        public long HandshakeRequestId;

        public StreamConsumerData(GuidId subscriptionId, QualifiedStreamId streamId, IStreamConsumerExtension streamConsumer, string? filterData)
        {
            SubscriptionId = subscriptionId;
            StreamId = streamId;
            StreamConsumer = streamConsumer;
            FilterData = filterData;
        }

        internal void SafeDisposeCursor(ILogger logger)
        {
            PendingBatch = null;
            if (Cursor is { } cursor)
            {
                Cursor = null;
                // 终止游标活动，并确保其不再针对此消费者数据重新启动。
                try
                {
                    cursor.Dispose();
                }
                catch (Exception ex)
                {
                    string? caller = null;
                    try
                    {
                        caller = $"Cursor.Dispose on stream {StreamId}, StreamConsumer {StreamConsumer} has thrown exception.";
                    }
                    catch { }
                    Utils.LogIgnoredException(logger, ex, caller);
                }
            }
        }
    }
}
