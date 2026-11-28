using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;
using AWSUtils.Tests.StorageTests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streaming.SQS.Streams;
using Orleans.Streams;
using OrleansAWSUtils.Storage;
using OrleansAWSUtils.Streams;
using TestExtensions;
using Xunit;
using Message = Amazon.SQS.Model.Message;

namespace AWSUtils.Tests.Streaming;

[TestCategory("AWS"), TestCategory("SQS")]
[Collection(TestEnvironmentFixture.DefaultCollection)]
[TestSuite("Functional")]
[TestProvider("SQS")]
[TestArea("Streaming")]
public class SQSDataAdapterTests : IAsyncLifetime
{
    private readonly ITestOutputHelper output;
    private readonly TestEnvironmentFixture fixture;
    private const int NumBatches = 20;
    private const int NumMessagesPerBatch = 20;
    private readonly string clusterId;
    public static readonly string SQS_STREAM_PROVIDER_NAME = "SQSAdapterTests";
    private readonly TimeSpan QueuePollRate = TimeSpan.FromSeconds(1);

    public SQSDataAdapterTests(ITestOutputHelper output, TestEnvironmentFixture fixture)
    {
        if (!AWSTestConstants.IsSqsAvailable)
        {
            throw Xunit.Sdk.SkipException.ForSkip("Empty connection string");
        }

        this.output = output;
        this.fixture = fixture;
        this.clusterId = MakeClusterId();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(AWSTestConstants.SqsConnectionString))
        {
            await SQSStreamProviderUtils.DeleteAllUsedQueues(
                SQS_STREAM_PROVIDER_NAME,
                this.clusterId,
                AWSTestConstants.SqsConnectionString,
                NullLoggerFactory.Instance);
        }
    }

    [Fact]
    public async Task SendAndReceiveFromSQS()
    {
        var options = new SqsOptions
        {
            ConnectionString = AWSTestConstants.SqsConnectionString,
            ReceiveMessageAttributes = new[] { "StreamId" }.ToList()
        };
        var clusterOptions = new ClusterOptions { ServiceId = this.clusterId };
        var dataAdapter = new StringOrIntSqsDataAdapter(fixture.Serializer);
        var adapterFactory = new SQSAdapterFactory(SQS_STREAM_PROVIDER_NAME, options, new HashRingStreamQueueMapperOptions(), new SimpleQueueCacheOptions(), Options.Create(clusterOptions), dataAdapter, NullLoggerFactory.Instance);
        adapterFactory.Init();
        await SendAndReceiveFromQueueAdapter(adapterFactory, TestContext.Current.CancellationToken);
    }

    private async Task SendAndReceiveFromQueueAdapter(
        IQueueAdapterFactory adapterFactory,
        CancellationToken cancellationToken)
    {
        IQueueAdapter adapter = await adapterFactory.CreateAdapter(cancellationToken);
        IQueueAdapterCache cache = adapterFactory.GetQueueAdapterCache();

        // 为每个队列创建接收器。
        IStreamQueueMapper mapper = adapterFactory.GetStreamQueueMapper();
        Dictionary<QueueId, IQueueAdapterReceiver> receivers = mapper.GetAllQueues().ToDictionary(queueId => queueId, adapter.CreateReceiver);
        Dictionary<QueueId, IQueueCache> caches = mapper.GetAllQueues().ToDictionary(queueId => queueId, cache.CreateQueueCache);

        await Task.WhenAll(receivers.Values.Select(receiver => receiver.Initialize(TimeSpan.FromSeconds(5))));

        // 使用两个流进行测试。
        Guid streamId1 = Guid.NewGuid();
        Guid streamId2 = Guid.NewGuid();

        int receivedBatches = 0;
        var streamsPerQueue = new ConcurrentDictionary<QueueId, HashSet<StreamId>>();

        // 读取线程（最多两个活动队列，因为只有两个流）。
        var work = new List<Task>();
        foreach (KeyValuePair<QueueId, IQueueAdapterReceiver> receiverKvp in receivers)
        {
            QueueId queueId = receiverKvp.Key;
            var receiver = receiverKvp.Value;
            var qCache = caches[queueId];
            Task task = Task.Run(async () =>
            {
                while (receivedBatches < NumBatches)
                {
                    var messages = (await receiver.GetQueueMessagesAsync(
                        SQSStorage.MAX_NUMBER_OF_MESSAGE_TO_PEEK,
                        cancellationToken)).ToArray();
                    if (!messages.Any())
                    {
                        await Task.Delay(QueuePollRate, cancellationToken);
                        continue;
                    }
                    foreach (var message in messages.Cast<SQSBatchContainer>())
                    {
                        streamsPerQueue.AddOrUpdate(queueId,
                            id => new HashSet<StreamId> { message.StreamId },
                            (id, set) =>
                            {
                                return new HashSet<StreamId>(set) { message.StreamId };
                            });
                        output.WriteLine("Queue {0} received message on stream {1}", queueId,
                            message.StreamId);
                        Assert.Equal(NumMessagesPerBatch / 2, message.GetEvents<int>().Count());  // "一半的事件为 int 类型"
                        Assert.Equal(NumMessagesPerBatch / 2, message.GetEvents<string>().Count());  // "一半的事件为 string 类型"
                    }
                    Interlocked.Add(ref receivedBatches, messages.Length);
                    qCache.AddToCache(messages);
                }
            }, cancellationToken);
            work.Add(task);
        }

        // 发送事件。
        List<object> events = CreateEvents(NumMessagesPerBatch);
        work.Add(Task.Run(async () =>
        {
            foreach (var streamId in Enumerable.Range(0, NumBatches).Select(i => i % 2 == 0 ? streamId1 : streamId2))
            {
                await AwaitWithCancellation(
                    adapter.QueueMessageBatchAsync(
                        StreamId.Create(streamId.ToString(), streamId),
                        events.Take(NumMessagesPerBatch).ToArray(),
                        null,
                        RequestContextExtensions.Export(this.fixture.DeepCopier)),
                    cancellationToken);
            }
        }, cancellationToken));
        await Task.WhenAll(work);

        // 等待所有消息均被消费。
        await Task.Delay(QueuePollRate * 2, cancellationToken);

        // 确保收到我们发送的全部内容。
        Assert.Equal(NumBatches, receivedBatches);

        // 检查所有事件是否都在缓存中，并且能否枚举它们。
        StreamSequenceToken firstInCache = new EventSequenceTokenV2(0);
        foreach (KeyValuePair<QueueId, HashSet<StreamId>> kvp in streamsPerQueue)
        {
            var receiver = receivers[kvp.Key];
            var qCache = caches[kvp.Key];

            foreach (StreamId streamGuid in kvp.Value)
            {
                // 读取缓存中属于此流的所有消息。
                var cursorResult = qCache.TryGetCacheCursor(streamGuid, firstInCache);
                Assert.Equal(QueueCacheCursorResultKind.Success, cursorResult.Kind);
                var cursor = cursorResult.Cursor;
                Assert.NotNull(cursor);
                int messageCount = 0;
                StreamSequenceToken? tenthInCache = null;
                StreamSequenceToken lastToken = firstInCache;
                while (MoveNext(cursor))
                {
                    Exception? ex;
                    messageCount++;
                    IBatchContainer? batch = cursor.GetCurrent(out ex);
                    Assert.NotNull(batch);
                    output.WriteLine("Token: {0}", batch.SequenceToken);
                    Assert.True(batch.SequenceToken.CompareTo(lastToken) >= 0, $"order check for event {messageCount}");
                    lastToken = batch.SequenceToken;
                    if (messageCount == 10)
                    {
                        tenthInCache = batch.SequenceToken;
                    }
                }
                output.WriteLine("On Queue {0} we received a total of {1} message on stream {2}", kvp.Key, messageCount, streamGuid);
                Assert.Equal(NumBatches / 2, messageCount);
                Assert.NotNull(tenthInCache);

                // 从第 10 条消息开始读取所有消息。
                cursorResult = qCache.TryGetCacheCursor(streamGuid, tenthInCache);
                Assert.Equal(QueueCacheCursorResultKind.Success, cursorResult.Kind);
                cursor = cursorResult.Cursor;
                Assert.NotNull(cursor);
                messageCount = 0;
                while (MoveNext(cursor))
                {
                    messageCount++;
                }
                output.WriteLine("On Queue {0} we received a total of {1} message on stream {2}", kvp.Key, messageCount, streamGuid);
                const int expected = NumBatches / 2 - 10 + 1; // 除前 10 项之外的全部项，再计入第 10 项（10 + 1）。
                Assert.Equal(expected, messageCount);
            }
        }
    }

    private static bool MoveNext(IQueueCacheCursor cursor)
    {
        var result = cursor.MoveNextWithResult();
        return result.Kind switch
        {
            QueueCacheCursorMoveResultKind.Success => true,
            QueueCacheCursorMoveResultKind.NoData => false,
            QueueCacheCursorMoveResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
            _ => throw new InvalidOperationException("The cursor move result is not initialized."),
        };
    }

    private static async Task AwaitWithCancellation(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch
        {
            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(
                    static completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            throw;
        }
    }

    private static List<object> CreateEvents(int count)
    {
        return Enumerable.Range(0, count).Select(i =>
        {
            if (i % 2 == 0)
            {
                return Random.Shared.Next(int.MaxValue) as object;
            }
            return Random.Shared.Next(int.MaxValue).ToString(CultureInfo.InvariantCulture);
        }).ToList();
    }

    internal static string MakeClusterId()
    {
        const string DeploymentIdFormat = "cluster-{0}";
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss-ffff", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, DeploymentIdFormat, now);
    }

    private class StringOrIntSqsDataAdapter : SQSDataAdapter
    {
        public StringOrIntSqsDataAdapter(Serializer serializer) : base(serializer)
        {
        }

        public override IBatchContainer FromQueueMessage(Message sqsMessage, long sequenceId)
        {
            // 示例从属性中提取 StreamId，而不是将其序列化到消息体中。
            if (!sqsMessage.MessageAttributes.TryGetValue("StreamId", out var streamIdStr))
                throw new DataException("SQS Message did not contain a StreamId attribute.");
            var streamId = StreamId.Parse(Encoding.UTF8.GetBytes(streamIdStr.StringValue));

            // 此构造的示例将字符串加引号发送，而 long 值不加引号。
            var events = sqsMessage.Body.Split(Environment.NewLine)
                .Select(x => (object)(x.StartsWith('"') ? x.Trim('"') : int.Parse(x)))
                .ToList();

            return new SQSBatchContainer(
                streamId,
                events,
                new Dictionary<string, object>(),
                new EventSequenceTokenV2(sequenceId)
            );
        }

        public override Message ToQueueMessage<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
        {
            // 此构造的示例将字符串加引号发送，而 long 值不加引号。
            var serializedData = string.Join(Environment.NewLine,
                events.Select(x => x is string ? $"\"{x}\"" : x?.ToString() ?? string.Empty));

            // 示例将 StreamId 作为属性包含在消息中。
            return new Message
            {
                MessageAttributes = new()
                    {
                        {
                            "StreamId",
                            new Amazon.SQS.Model.MessageAttributeValue
                            {
                                DataType = "String",
                                StringValue = streamId.ToString()
                            }
                        }
                    },
                Body = serializedData
            };
        }
    }
}
