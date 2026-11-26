using System;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.Streams
{
    [Serializable]
    internal enum StreamConsumerDataState
    {
        Active, // 表示事件正在被主动交付给该消费者。
        Inactive, // 显示事件并非主动交付给消费者。 如果适应器在这个消费者流上产生任何活动,代理器将需要开始交付活动
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
        /// 订阅此文件所处理的最后一批( 交付或过滤) 的序列符号 。
        /// 由拉动剂定期扫描 用来计算以送货为基础的检查站水印
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
                // 杀死光标活动,并确保它不再从此消费数据开始。
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
