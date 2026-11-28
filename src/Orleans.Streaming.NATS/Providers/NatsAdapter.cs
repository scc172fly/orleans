using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Streaming.NATS;

internal sealed class NatsAdapter(
    string providerName,
    NatsOptions options,
    ILoggerFactory loggerFactory,
    Serializer serializer,
    NatsConnectionManager natsConnectionManager) : IQueueAdapter
{
    public string Name => providerName;
    public bool IsRewindable => false; // 以后将使其支持回溯。
    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId) =>
        NatsQueueAdapterReceiver.Create(providerName, loggerFactory, natsConnectionManager, queueId.GetNumericId(),
            options, serializer);

    public async Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token,
        Dictionary<string, object>? requestContext)
    {
        await natsConnectionManager.EnqueueMessage(CreateMessage(serializer, streamId, events, requestContext));
    }

    internal static NatsStreamMessage CreateMessage<T>(Serializer serializer, StreamId streamId, IEnumerable<T> events,
        Dictionary<string, object>? requestContext)
    {
        var batchContainer = new NatsBatchContainer(streamId, events.Cast<object>().ToArray(), requestContext);
        var raw = serializer.GetSerializer<NatsBatchContainer>().SerializeToArray(batchContainer);

        // 请求上下文包含在由 Orleans 序列化的有效载荷中，从而保留其中各值的类型。
        return new NatsStreamMessage
        {
            StreamId = streamId,
            Payload = raw
        };
    }
}
