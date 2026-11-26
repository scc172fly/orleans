
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Hosting;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// 简单发电机
    /// 生成一个可配置事件数的单一流。 每个民意测验都产生一个事件 。
    /// </summary>
    internal class SimpleGenerator : IStreamGenerator
    {
        private SimpleGeneratorOptions options = null!; // 阅读前设置在配置中 。
        private StreamId streamId;
        private int sequenceId;

        /// <inheritdoc />
        public void Configure(IServiceProvider serviceProvider, IStreamGeneratorConfig generatorConfig)
        {
            var cfg = generatorConfig as SimpleGeneratorOptions;
            if (cfg == null)
            {
                throw new ArgumentOutOfRangeException(nameof(generatorConfig));
            }
            options = cfg;
            sequenceId = 0;
            streamId = StreamId.Create(options.StreamNamespace, Guid.NewGuid());
        }

        /// <summary>
        /// 产生一个单一事件
        /// </summary>
        public bool TryReadEvents(DateTime utcNow, int maxCount, out List<IBatchContainer> events)
        {
            events = new List<IBatchContainer>();
            if (sequenceId >= this.options.EventsInStream)
            {
                return false;
            }

            for (int i = 0; i < maxCount; i++)
            {
                if (!TryGenerateBatch(out var batch))
                    break;
                events.Add(batch);
            }

            return true;
        }

        private bool TryGenerateBatch([NotNullWhen(true)] out GeneratedBatchContainer? batch)
        {
            batch = null;
            if (sequenceId >= this.options.EventsInStream)
            {
                return false;
            }
            sequenceId++;
            var evt = new GeneratedEvent
            {
                // 如果这是最后生成的事件, 请标为它本身, 所以测试 grains 就会报告结果 。
                EventType = (sequenceId != this.options.EventsInStream)
                        ? GeneratedEvent.GeneratedEventType.Fill
                        : GeneratedEvent.GeneratedEventType.Report
            };
            batch = new GeneratedBatchContainer(streamId, evt, new EventSequenceTokenV2(sequenceId));
            return true;
        }
    }
}
