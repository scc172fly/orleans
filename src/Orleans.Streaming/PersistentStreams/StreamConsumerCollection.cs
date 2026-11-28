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
        private readonly Dictionary<GuidId, StreamConsumerData> queueData; // 单个流的消费者映射：从 Guid ConsumerId 映射到 StreamConsumerData。
        [Id(1)]
        private DateTime lastActivityTime;

        [Id(2)]
        public bool StreamRegistered { get; set; }

        // 不进行序列化，仅用于运行时跟踪正在进行的注册。
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
            // 从拉取代理的角度，如果满足以下条件，则认为流（及其所有消费者）处于非活动状态：
            // 1) 最近一个 inactivityPeriod 内没有收到该流的新事件；
            // 2) 该流的所有消费者当前均处于非活动状态（即所有游标均处于非活动状态），
            // 也就是说，适配器缓存中没有可供这些消费者使用的数据；
            // 3) 消费者握手已完成，且其位置已协调一致。
            if (now - lastActivityTime < inactivityPeriod) return false;
            return !queueData.Values.Any(data => data.PendingHandshakes != 0 || data.HasUnresolvedHandshake
                || data.State.Equals(StreamConsumerDataState.Active));
        }
    }
}
