using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 为一个流生成数据。
    /// </summary>
    public partial class SimpleStreamEventDataGenerator : IStreamDataGenerator<EventData>
    {
        /// <inheritdoc />
        public StreamId StreamId { get; set; }

        /// <inheritdoc />
        public IIntCounter SequenceNumberCounter { set; private get; } = null!;
        /// <inheritdoc />
        public bool ShouldProduce { private get; set; }

        private readonly ILogger logger;
        private readonly DeepCopier deepCopier;
        private readonly Serializer serializer;

        /// <summary>
        /// 初始化 <see cref="SimpleStreamEventDataGenerator"/> 类的新实例。
        /// </summary>
        /// <param name="streamId">要为其生成事件的流。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="deepCopier">请求上下文复制器。</param>
        /// <param name="serializer">用于编码生成事件的序列化器。</param>
        public SimpleStreamEventDataGenerator(StreamId streamId, ILogger<SimpleStreamEventDataGenerator> logger, DeepCopier deepCopier, Serializer serializer)
        {
            this.StreamId = streamId;
            this.logger = logger;
            this.ShouldProduce = true;
            this.deepCopier = deepCopier;
            this.serializer = serializer;
        }

        /// <inheritdoc />
        public bool TryReadEvents(int maxCount, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<EventData>? events)
        {
            if (!this.ShouldProduce)
            {
                events = null;
                return false;
            }
            int count = maxCount;
            List<EventData> eventDataList = new List<EventData>(maxCount);
            while (count-- > 0)
            {
                this.SequenceNumberCounter.Increment();

                // 创建消息体为空的 EventData 实例。随后将根据批次容器的上下文
                // 设置消息体。由于需要显式设置
                // 偏移量、序列号和分区键等由消息代理所有的属性，
                // 因此使用模型工厂创建实例，避免必须通过
                // 底层 AMQP 消息直接设置这些属性。
                var eventData = EventHubsModelFactory.EventData(
                        eventBody: BinaryData.Empty,
                        partitionKey: StreamId.GetKeyAsString(),
                        offsetString: DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture),
                        sequenceNumber: this.SequenceNumberCounter.Value);

                EventHubBatchContainer.UpdateEventData(
                    eventData,
                    this.serializer,
                    this.StreamId,
                    GenerateEvent(this.SequenceNumberCounter.Value),
                    RequestContextExtensions.Export(this.deepCopier));

                eventDataList.Add(eventData);
                LogInfoGenerateData(this.SequenceNumberCounter.Value, this.StreamId);
            }

            events = eventDataList;
            return eventDataList.Count > 0;
        }

        private static IEnumerable<int> GenerateEvent(int sequenceNumber)
        {
            return [sequenceNumber];
        }

        /// <summary>
        /// 创建特定流的事件数据生成器工厂。
        /// </summary>
        /// <param name="services">用于激活生成器的服务提供程序。</param>
        /// <returns>为流创建事件数据生成器的工厂。</returns>
        public static Func<StreamId, IStreamDataGenerator<EventData>> CreateFactory(IServiceProvider services)
        {
            return (streamId) => ActivatorUtilities.CreateInstance<SimpleStreamEventDataGenerator>(services, streamId);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Generate data of SequenceNumber {SequenceNumber} for stream {StreamId}"
        )]
        private partial void LogInfoGenerateData(int sequenceNumber, StreamId streamId);
    }

    /// <summary>
    /// EHPartitionDataGenerator 为一个 Event Hub 分区生成数据，可以包含来自不同流的数据。
    /// </summary>
    public partial class EventHubPartitionDataGenerator : IDataGenerator<EventData>, IStreamDataGeneratingController
    {
        // 同一分区中的不同流应使用相同的 sequenceNumberCounter。
        private readonly EventDataGeneratorStreamOptions options;
        private readonly IntCounter sequenceNumberCounter = new IntCounter();
        private readonly ILogger logger;
        private readonly Func<StreamId, IStreamDataGenerator<EventData>> generatorFactory;
        private readonly List<IStreamDataGenerator<EventData>> generators;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="options"></param>
        /// <param name="generatorFactory"></param>
        /// <param name="logger"></param>
        public EventHubPartitionDataGenerator(EventDataGeneratorStreamOptions options, Func<StreamId, IStreamDataGenerator<EventData>> generatorFactory, ILogger logger)
        {
            this.options = options;
            this.generatorFactory = generatorFactory;
            this.generators = new List<IStreamDataGenerator<EventData>>();
            this.logger = logger;
        }
        /// <inheritdoc />
        public void AddDataGeneratorForStream(StreamId streamId)
        {
            var generator = this.generatorFactory(streamId);
            generator.SequenceNumberCounter = sequenceNumberCounter;
            LogInfoOnStreamSetup(streamId);
            this.generators.Add(generator);
        }
        /// <inheritdoc />
        public void StopProducingOnStream(StreamId streamId)
        {
            this.generators.ForEach(generator =>
            {
                if (generator.StreamId.Equals(streamId))
                {
                    generator.ShouldProduce = false;
                    LogInfoOnStreamStop(streamId);
                }
            });
        }
        /// <inheritdoc />
        public bool TryReadEvents(int maxCount, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<EventData>? events)
        {
            if (this.generators.Count == 0)
            {
                events = new List<EventData>();
                return false;
            }
            var eventDataList = new List<EventData>();
            var iterator = this.generators.AsEnumerable().GetEnumerator();
            var batchCount = maxCount / this.generators.Count;
            batchCount = batchCount == 0 ? batchCount + 1 : batchCount;
            while (eventDataList.Count < maxCount)
            {
                // 到达列表末尾时，将迭代器重置到开头。
                if (!iterator.MoveNext())
                {
                    iterator.Reset();
                    iterator.MoveNext();
                }
                IEnumerable<EventData>? eventData;
                var remainingCount = maxCount - eventDataList.Count;
                var count = remainingCount > batchCount ? batchCount : remainingCount;
                if (iterator.Current.TryReadEvents(count, out eventData))
                {
                    foreach (var data in eventData)
                    {
                        eventDataList.Add(data);
                    }
                }
            }
            iterator.Dispose();
            events = eventDataList.AsEnumerable();
            return eventDataList.Count > 0;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Data generator set up on stream {StreamId}."
        )]
        private partial void LogInfoOnStreamSetup(StreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Stop producing data on stream {StreamId}."
        )]
        private partial void LogInfoOnStreamStop(StreamId streamId);
    }
}
