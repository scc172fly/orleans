using Orleans.Runtime;
using Azure.Messaging.EventHubs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 配置数据生成器的偶形接收器
    /// </summary>
    public class EventHubPartitionGeneratorReceiver : IEventHubReceiver
    {
        private readonly IDataGenerator<EventData> generator;
        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="generator"></param>
        public EventHubPartitionGeneratorReceiver(IDataGenerator<EventData> generator)
        {
            this.generator = generator;
        }
        /// <inheritdoc />
        public async Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime)
            => await ReceiveAsync(maxCount, waitTime, CancellationToken.None);

        /// <inheritdoc />
        public async Task<IEnumerable<EventData>> ReceiveAsync(
            int maxCount,
            TimeSpan waitTime,
            CancellationToken cancellationToken)
        {
            IEnumerable<EventData>? events;
            //模拟真实生活反应时间
            await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
            if (generator.TryReadEvents(maxCount, out events))
            {
                return events;
            }
            //如果未产生事件,请等待 waitTime 通过
            await Task.Delay(waitTime, cancellationToken);
            return new List<EventData>().AsEnumerable();
        }

        /// <inheritdoc />
        public void StopProducingOnStream(StreamId streamId)
        {
            (this.generator as IStreamDataGeneratingController)?.StopProducingOnStream(streamId);
        }

        /// <inheritdoc />
        public void ConfigureDataGeneratorForStream(StreamId streamId)
        {
            (this.generator as IStreamDataGeneratingController)?.AddDataGeneratorForStream(streamId);
        }

        /// <inheritdoc />
        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }
    }
}
