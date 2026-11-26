using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.Streams
{
    [Serializable]
    [GenerateSerializer]
    internal sealed class StreamConsumerCollection
    {
        [Id(0)]
        private readonly Dictionary<GuidId, StreamConsumerData> queueData; // 一张一流消费者地图:取自《指南》 ConsumerId 至 StreamConsumerData
        [Id(1)]
        private DateTime lastActivityTime;

        [Id(2)]
        public bool StreamRegistered { get; set; }

        // 没有序列化 -- -- 运行时间只限飞行中的登记跟踪。
        [NonSerialized]
        public Task? RegistrationTask;

        public StreamConsumerCollection(DateTime now)
        {
            queueData = new Dictionary<GuidId, StreamConsumerData>();
            lastActivityTime = now;
        }

        public StreamConsumerData AddConsumer(GuidId subscriptionId, QualifiedStreamId streamId, IStreamConsumerExtension streamConsumer, string? filterData, DateTime now)
        {
            var consumerData = new StreamConsumerData(subscriptionId, streamId, streamConsumer, filterData);
            queueData.Add(subscriptionId, consumerData);
            lastActivityTime = now;
            return consumerData;
        }

        public bool RemoveConsumer(GuidId subscriptionId, ILogger logger)
        {
            if (!queueData.Remove(subscriptionId, out var consumer)) return false;

            consumer.HandshakeRequestId++;
            consumer.HandshakeGeneration++;
            consumer.SafeDisposeCursor(logger);
            return true;
        }

        public bool Contains(GuidId subscriptionId)
        {
            return queueData.ContainsKey(subscriptionId);
        }

        public bool TryGetConsumer(GuidId subscriptionId, [MaybeNullWhen(false)] out StreamConsumerData data)
        {
            return queueData.TryGetValue(subscriptionId, out data);
        }

        public IEnumerable<StreamConsumerData> AllConsumers()
        {
            return queueData.Values;
        }

        public void DisposeAll(ILogger logger)
        {
            foreach (StreamConsumerData consumer in queueData.Values)
            {
                consumer.SafeDisposeCursor(logger);
            }
            queueData.Clear();
        }

        public int Count
        {
            get { return queueData.Count; }
        }

        public void RefreshActivity(DateTime now)
        {
            lastActivityTime = now;
        }

        public bool IsInactive(DateTime now, TimeSpan inactivityPeriod)
        {
            // 从拉动剂的角度考虑(与其所有消费者一起)流不活动,如果:
            // (1) 最后一个 inactivityPeriod没有收到该流的新事件
            // (2) 该流的所有消费者目前都处于不活动状态(即所有光标都不活动) - 
            //    意思是这些消费者在适配器缓存中 没有任何用处
            // (3) 消费者握手已经完成,其立场得到协调。
            if (now - lastActivityTime < inactivityPeriod) return false;
            return !queueData.Values.Any(data => data.PendingHandshakes != 0 || data.HasUnresolvedHandshake
                || data.State.Equals(StreamConsumerDataState.Active));
        }
    }
}
