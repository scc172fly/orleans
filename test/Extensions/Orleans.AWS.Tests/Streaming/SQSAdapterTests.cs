using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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

namespace AWSUtils.Tests.Streaming
{
    /// <summary>
    /// 测试 SQS 队列适配器功能, 以便通过 Orleans 流发送和接收信件。
    /// </summary>
    [TestCategory("AWS"), TestCategory("SQS")]
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    [TestSuite("Functional")]
    [TestProvider("SQS")]
    [TestArea("Streaming")]
    public class SQSAdapterTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper output;
        private readonly TestEnvironmentFixture fixture;
        private const int NumBatches = 20;
        private const int NumMessagesPerBatch = 20;
        private readonly string clusterId;
        private bool usesFifoQueue;
        public static readonly string SQS_STREAM_PROVIDER_NAME = "SQSAdapterTests";
        private readonly TimeSpan QueuePollRate = TimeSpan.FromSeconds(1);

        public SQSAdapterTests(ITestOutputHelper output, TestEnvironmentFixture fixture)
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
                    NullLoggerFactory.Instance,
                    usesFifoQueue);
            }
        }

        [Fact]
        public async Task SendAndReceiveFromSQS()
        {
            var options = new SqsOptions
            {
                ConnectionString = AWSTestConstants.SqsConnectionString,
            };
            var clusterOptions = new ClusterOptions { ServiceId = this.clusterId };
            var dataAdapter = new SQSDataAdapter(fixture.Serializer);
            var adapterFactory = new SQSAdapterFactory(SQS_STREAM_PROVIDER_NAME, options, new HashRingStreamQueueMapperOptions(), new SimpleQueueCacheOptions(), Options.Create(clusterOptions), dataAdapter, NullLoggerFactory.Instance);
            adapterFactory.Init();
            await SendAndReceiveFromQueueAdapter(adapterFactory, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ShutdownReleasesFifoMessages()
        {
            usesFifoQueue = true;
            var options = new SqsOptions
            {
                ConnectionString = AWSTestConstants.SqsConnectionString,
                FifoQueue = true,
                VisibilityTimeoutSeconds = 60,
            };
            var clusterOptions = new ClusterOptions { ServiceId = this.clusterId };
            var queueMapperOptions = new HashRingStreamQueueMapperOptions { TotalQueueCount = 1 };
            var dataAdapter = new SQSDataAdapter(fixture.Serializer);
            var adapterFactory = new SQSAdapterFactory(
                SQS_STREAM_PROVIDER_NAME,
                options,
                queueMapperOptions,
                new SimpleQueueCacheOptions(),
                Options.Create(clusterOptions),
                dataAdapter,
                NullLoggerFactory.Instance);
            adapterFactory.Init();

            var cancellationToken = TestContext.Current.CancellationToken;
            var adapter = await adapterFactory.CreateAdapter();
            var queueId = Assert.Single(adapterFactory.GetStreamQueueMapper().GetAllQueues());
            var receiver = adapter.CreateReceiver(queueId);
            await receiver.Initialize(TimeSpan.FromSeconds(10), cancellationToken);

            var streamId = StreamId.Create("handoff", Guid.NewGuid());
            await adapter.QueueMessageBatchAsync(streamId, [42], null, null);
            var received = await WaitForMessage(receiver, "initial receiver", cancellationToken);
            Assert.Equal(42, Assert.Single(received.GetEvents<int>()).Item1);

            await receiver.Shutdown(TimeSpan.FromSeconds(10), cancellationToken);

            var replacement = adapter.CreateReceiver(queueId);
            await replacement.Initialize(TimeSpan.FromSeconds(10), cancellationToken);
            var redelivered = await WaitForMessage(replacement, "replacement receiver after handoff", cancellationToken);
            Assert.Equal(42, Assert.Single(redelivered.GetEvents<int>()).Item1);

            await replacement.MessagesDeliveredAsync([redelivered], cancellationToken);
            await replacement.Shutdown(TimeSpan.FromSeconds(10), cancellationToken);
        }

        private async Task<IBatchContainer> WaitForMessage(
            IQueueAdapterReceiver receiver,
            string phase,
            CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var messages = await receiver.GetQueueMessagesAsync(1, cancellationToken);
                if (messages.Count > 0)
                {
                    return Assert.Single(messages);
                }

                await Task.Delay(QueuePollRate, cancellationToken);
            }

            throw new TimeoutException($"Timed out after {timeout} waiting for one SQS message during {phase}; observed 0.");
        }

        private async Task SendAndReceiveFromQueueAdapter(
            IQueueAdapterFactory adapterFactory,
            CancellationToken cancellationToken)
        {
            IQueueAdapter adapter = await adapterFactory.CreateAdapter(cancellationToken);
            IQueueAdapterCache cache = adapterFactory.GetQueueAdapterCache();

            // 每个队列创建接收器
            IStreamQueueMapper mapper = adapterFactory.GetStreamQueueMapper();
            Dictionary<QueueId, IQueueAdapterReceiver> receivers = mapper.GetAllQueues().ToDictionary(queueId => queueId, adapter.CreateReceiver);
            Dictionary<QueueId, IQueueCache> caches = mapper.GetAllQueues().ToDictionary(queueId => queueId, cache.CreateQueueCache);

            await Task.WhenAll(receivers.Values.Select(receiver => receiver.Initialize(TimeSpan.FromSeconds(5))));

            // 使用 2 流测试
            Guid streamId1 = Guid.NewGuid();
            Guid streamId2 = Guid.NewGuid();

            int receivedBatches = 0;
            var streamsPerQueue = new ConcurrentDictionary<QueueId, HashSet<StreamId>>();

            // 读者线(大多数为2个活动队列,因为只有两个串流)
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
                            Assert.Equal(NumMessagesPerBatch / 2, message.GetEvents<int>().Count());  // "如果事件是半个"
                            Assert.Equal(NumMessagesPerBatch / 2, message.GetEvents<string>().Count());  // "如果事情是串通的"
                        }
                        Interlocked.Add(ref receivedBatches, messages.Length);
                        qCache.AddToCache(messages);
                    }
                }, cancellationToken);
                work.Add(task);
            }

            // 发送事件
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

            // 等待一切被消耗。
            await Task.Delay(QueuePollRate * 2, cancellationToken);

            // 确保我们把寄送的东西都拿回来
            Assert.Equal(NumBatches, receivedBatches);

            // 检查是否所有事件都在缓存中, 我们可以通过它们来计算
            StreamSequenceToken firstInCache = new EventSequenceTokenV2(0);
            foreach (KeyValuePair<QueueId, HashSet<StreamId>> kvp in streamsPerQueue)
            {
                var receiver = receivers[kvp.Key];
                var qCache = caches[kvp.Key];

                foreach (StreamId streamGuid in kvp.Value)
                {
                    // 读取流的缓存中的所有信件
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

                    // 读取来自 第10次的所有消息
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
                    const int expected = NumBatches / 2 - 10 + 1; // 除前10名外,包括第10名(10+1)
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
    }
}
