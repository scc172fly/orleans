
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
    /// 简单生成器
    /// 生成一个包含可配置数量事件的单一流。每次轮询生成一个事件。
    /// </summary>
    internal class SimpleGenerator : IStreamGenerator
    {
        private SimpleGeneratorOptions options = null!; // 读取前在 Configure 中设置。
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
        /// 在生成的事件数量达到配置值之前，每次返回一个生成的事件。
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
                // 如果这是最后一个生成的事件，则将其标记为最后一个，以便测试 Grain 知道应报告结果。
                EventType = (sequenceId != this.options.EventsInStream)
                        ? GeneratedEvent.GeneratedEventType.Fill
                        : GeneratedEvent.GeneratedEventType.Report
            };
            batch = new GeneratedBatchContainer(streamId, evt, new EventSequenceTokenV2(sequenceId));
            return true;
        }
    }
}
