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
    /// 生成一个流的数据
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
        /// <param name="streamId">要生成事件的串流。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="deepCopier">请求的上下文复印机。</param>
        /// <param name="serializer">用于编码生成事件的序列器。</param>
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

                // 以空体创建 EventData 实例。此主体稍后会设置
                // 由于需要明确规定
                // 经纪人拥有的财产,如冲抵、序列号和分隔键,
                // a 实例是使用模型工厂创建的,这样就不需要设置
                // 直接通过基本AMQP信息。
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
        /// 创建了一座工厂,用于特定流事件数据生成器。
        /// </summary>
        /// <param name="services">用于启动发电机的服务提供商。</param>
        /// <returns>工厂,为流创建事件数据生成器。</returns>
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
    /// EHPartitionDataGenerator 生成 EH 分区数据,其中可包括来自不同流流的数据
    /// </summary>
    public partial class EventHubPartitionDataGenerator : IDataGenerator<EventData>, IStreamDataGeneratingController
    {
        //在同一分区中, 同一分隔段的相差流应该使用相同的 sequenceNumberCounter
        private readonly EventDataGeneratorStreamOptions options;
        private readonly IntCounter sequenceNumberCounter = new IntCounter();
        private readonly ILogger logger;
        private readonly Func<StreamId, IStreamDataGenerator<EventData>> generatorFactory;
        private readonly List<IStreamDataGenerator<EventData>> generators;

        /// <summary>
        /// 构造建构器
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
                //如果达到列表末尾,则将替换器重新设置到头部
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
