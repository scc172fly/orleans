using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using Xunit;

#pragma warning disable CS0618 // 此测试套件验证旧版游标 API 与其替代 API 并存时的兼容性。

namespace UnitTests.OrleansRuntime.Streams
{
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Streaming")]
    public class PooledQueueCacheTests
    {
        private const int PooledBufferCount = 8;
        private const int PooledBufferSize = 1 << 10; // 1K
        private const int MessageSize = 1 << 7; // 128
        private const int MessagesPerBuffer = 8;
        private const string TestStreamNamespace = "blarg";

        private class TestQueueMessage
        {
            private static readonly byte[] FixedMessage = new byte[MessageSize];
            public StreamId StreamId;
            public long SequenceNumber;
            public byte[] Data { get; init; } = FixedMessage;
            public DateTime EnqueueTimeUtc = DateTime.UtcNow;
        }

        private sealed class MutatingSequenceToken : StreamSequenceToken
        {
            private readonly long _sequenceNumber;
            private readonly int _mutationRead;
            private readonly Action _mutation;
            private int _sequenceReads;

            public MutatingSequenceToken(long sequenceNumber, int mutationRead, Action mutation)
            {
                _sequenceNumber = sequenceNumber;
                _mutationRead = mutationRead;
                _mutation = mutation;
            }

            public override long SequenceNumber
            {
                get
                {
                    if (Interlocked.Increment(ref _sequenceReads) == _mutationRead)
                    {
                        _mutation();
                    }

                    return _sequenceNumber;
                }
                protected set => throw new NotSupportedException();
            }

            public override int EventIndex
            {
                get => 0;
                protected set => throw new NotSupportedException();
            }

            public override int CompareTo(StreamSequenceToken? other)
                => other is null
                    ? 1
                    : _sequenceNumber != other.SequenceNumber
                        ? _sequenceNumber.CompareTo(other.SequenceNumber)
                        : EventIndex.CompareTo(other.EventIndex);

            public override bool Equals(StreamSequenceToken? other)
                => other is not null
                    && _sequenceNumber == other.SequenceNumber
                    && EventIndex == other.EventIndex;
        }

        [GenerateSerializer]
        public class TestBatchContainer : IBatchContainer
        {
            [Id(0)]
            public StreamId StreamId { get; set; }

            [Id(1)]
            public StreamSequenceToken SequenceToken { get; set; } = null!;

            [Id(2)]
            public byte[] Data { get; set; } = null!;

            public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
            {
                throw new NotImplementedException();
            }

            public bool ImportRequestContext()
            {
                throw new NotImplementedException();
            }
        }

        private class TestCacheDataAdapter : ICacheDataAdapter
        {
            public IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
            {
                //反序列化有效负载
                int readOffset = 0;
                ArraySegment<byte> payload = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset);

                return new TestBatchContainer
                {
                    StreamId = cachedMessage.StreamId,
                    SequenceToken = GetSequenceToken(ref cachedMessage),
                    Data = payload.ToArray()
                };
            }

            public StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage)
            {
                return new EventSequenceTokenV2(cachedMessage.SequenceNumber);
            }

        }

        private class CachedMessageConverter
        {
            private readonly IObjectPool<FixedSizeBuffer> bufferPool;
            private readonly IEvictionStrategy evictionStrategy;
            private FixedSizeBuffer currentBuffer = null!;


            public CachedMessageConverter(IObjectPool<FixedSizeBuffer> bufferPool, IEvictionStrategy evictionStrategy)
            {
                this.bufferPool = bufferPool;
                this.evictionStrategy = evictionStrategy;
            }

            public CachedMessage ToCachedMessage(TestQueueMessage queueMessage, DateTime dequeueTimeUtc)
            {
                StreamPosition streamPosition = GetStreamPosition(queueMessage);
                return new CachedMessage
                {
                    StreamId = streamPosition.StreamId,
                    SequenceNumber = queueMessage.SequenceNumber,
                    EnqueueTimeUtc = queueMessage.EnqueueTimeUtc,
                    DequeueTimeUtc = dequeueTimeUtc,
                    Segment = SerializeMessageIntoPooledSegment(queueMessage),
                };
            }

            private StreamPosition GetStreamPosition(TestQueueMessage queueMessage)
            {
                StreamSequenceToken sequenceToken = new EventSequenceTokenV2(queueMessage.SequenceNumber);
                return new StreamPosition(queueMessage.StreamId, sequenceToken);
            }

            private ArraySegment<byte> SerializeMessageIntoPooledSegment(TestQueueMessage queueMessage)
            {
                // 序列化有效载荷
                int size = SegmentBuilder.CalculateAppendSize(queueMessage.Data);

                // 从当前缓冲块获取片段
                ArraySegment<byte> segment;
                if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
                {
                    // 没有块或块已满，获取新块并重试。
                    var newBuffer = bufferPool.Allocate();
                    // 如果使用空缓冲块仍然失败，则请求的大小过大；将
                    // 未使用的缓冲块归还给池并报告失败，避免泄漏永远不会提交的缓冲块。
                    if (!newBuffer.TryGetSegment(size, out segment))
                    {
                        newBuffer.Dispose();
                        string errmsg = string.Format(CultureInfo.InvariantCulture,
                            "Message size is too big. MessageSize: {0}", size);
                        throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                    }
                    currentBuffer = newBuffer;
                    // 调用 EvictionStrategy 的 OnBlockAllocated 方法。
                    this.evictionStrategy.OnBlockAllocated(currentBuffer);
                }
                // 将命名空间、偏移量、分区键、属性及有效载荷编码到分段中。
                int writeOffset = 0;
                SegmentBuilder.Append(segment, ref writeOffset, queueMessage.Data);
                return segment;
            }
        }

        /// <summary>
        /// 使用两个流填充缓存。
        /// 获取指向每个流起始位置的有效游标。
        /// 遍历每个游标，直到相应流不再有数据。
        /// 交替向缓存添加消息和遍历游标。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void GoldenPathTest()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            RunGoldenPath(cache, converter, 111);
        }

        /// <summary>
        /// 运行正常路径测试，然后清除缓存，再次运行正常路径测试。
        /// 目标是确保清除全部数据后，缓存能够正确清理资源。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void CacheDrainTest()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            int startSequenceNuber = 222;
            startSequenceNuber = RunGoldenPath(cache, converter, startSequenceNuber);
            RunGoldenPath(cache, converter, startSequenceNuber);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void EarliestAvailableCursorStartsAtOldestRetainedMessageForStream()
        {
            var (cache, converter) = CreateCache();
            var targetStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var otherStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = targetStream, SequenceNumber = 1 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = otherStream, SequenceNumber = 2 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = targetStream, SequenceNumber = 3 }, now),
            ], now);

            var result = cache.TryGetCursorAtPosition(targetStream, StreamSubscriptionStartPosition.EarliestAvailable);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;

            Assert.True(cache.TryGetNextMessage(cursor, out var first));
            Assert.Equal(1, first.SequenceToken.SequenceNumber);
            Assert.True(cache.TryGetNextMessage(cursor, out var second));
            Assert.Equal(3, second.SequenceToken.SequenceNumber);
            Assert.False(cache.TryGetNextMessage(cursor, out _));
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void GetCursorPreservesLegacyCursorBehavior()
        {
            var (cache, converter) = CreateCache();
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 1 }, now),
            ], now);

            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(1));

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(1, message.SequenceToken.SequenceNumber);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void EarliestAvailableCursorWaitsForFirstFutureMessageWhenStreamIsNotRetained()
        {
            var (cache, converter) = CreateCache();
            var targetStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var otherStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = otherStream, SequenceNumber = 100 }, now),
            ], now);
            var result = cache.TryGetCursorAtPosition(targetStream, StreamSubscriptionStartPosition.EarliestAvailable);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;

            Assert.False(cache.TryGetNextMessage(cursor, out _));

            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = otherStream, SequenceNumber = 101 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = targetStream, SequenceNumber = 1 }, now),
            ], now);

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(1, message.SequenceToken.SequenceNumber);
            Assert.False(cache.TryGetNextMessage(cursor, out _));
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void EarliestAvailableCursorCreatedOnEmptyCacheWaitsForFirstMatchingMessage()
        {
            var (cache, converter) = CreateCache();
            var targetStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var otherStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var result = cache.TryGetCursorAtPosition(targetStream, StreamSubscriptionStartPosition.EarliestAvailable);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;
            var now = DateTime.UtcNow;

            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = otherStream, SequenceNumber = 1 }, now),
            ], now);
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = targetStream, SequenceNumber = 2 }, now),
            ], now);

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(2, message.SequenceToken.SequenceNumber);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void EarliestAvailableCursorRecoversWhenInitialMessageIsPurged()
        {
            var (cache, converter) = CreateCache();
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 1 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 2 }, now),
            ], now);
            var result = cache.TryGetCursorAtPosition(stream, StreamSubscriptionStartPosition.EarliestAvailable);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;

            cache.RemoveOldestMessage();

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(2, message.SequenceToken.SequenceNumber);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void EarliestAvailableWaitingCursorSurvivesAnchorPurge()
        {
            var (cache, converter) = CreateCache();
            var targetStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var otherStream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = otherStream, SequenceNumber = 100 }, now),
            ], now);
            var result = cache.TryGetCursorAtPosition(targetStream, StreamSubscriptionStartPosition.EarliestAvailable);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            cache.RemoveOldestMessage();
            Assert.False(cache.TryGetNextMessage(cursor, out _));
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = targetStream, SequenceNumber = 1 }, now),
            ], now);

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(1, message.SequenceToken.SequenceNumber);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void LatestCursorDoesNotDeliverRetainedMessage()
        {
            var (cache, converter) = CreateCache();
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 1 }, now),
            ], now);

            var result = cache.TryGetCursorAtPosition(stream, StreamSubscriptionStartPosition.Latest);
            Assert.Equal(QueueCacheCursorResultKind.Success, result.Kind);
            Assert.NotNull(result.Cursor);
            var cursor = result.Cursor;

            Assert.False(cache.TryGetNextMessage(cursor, out _));
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 2 }, now),
            ], now);
            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(2, message.SequenceToken.SequenceNumber);
        }

        /// <summary>
        /// 针对 https://github.com/dotnet/orleans/issues/10263 的回归测试。
        /// 在提交到缓存之前，将批次编码到池化缓冲区中。当这样的
        /// 批次在缓存为空时跨越多个 <see cref="FixedSizeBuffer"/>，
        /// 逐出策略不得回收仍保存同一批次中正在处理（尚未提交）
        /// 的片段的缓冲区。此前会发生这种情况：缓冲区被归还给后进先出（LIFO）
        /// 池，同一批次中的下一次分配立即取回该缓冲区，并从偏移量 0 开始覆盖
        /// 先前的消息，导致其有效负载被悄然损坏（之后表现为解码时的
        /// ArgumentOutOfRangeException）。此测试写入不同的有效负载，并
        /// 断言将一个跨多个缓冲区的批次添加到空缓存后，每条消息均保持完好。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void MultiBufferBatchFromEmptyCacheKeepsPayloadsIntact()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            const int startSequenceNumber = 42;
            // 消息数量足以让单个批次跨越多个缓冲区。
            const int messageCount = MessagesPerBuffer * 5;
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());

            var messages = Enumerable.Range(0, messageCount)
                .Select(i => new TestQueueMessage
                {
                    StreamId = stream,
                    SequenceNumber = startSequenceNumber + i,
                    Data = CreatePayload(i),
                })
                .ToList();

            // 先将整个批次编码到池化缓冲区，再提交——这与实际
            // EventHub、Memory 和 Generator 缓存采用的顺序相同，也正是触发数据损坏的顺序。
            var utcNow = DateTime.UtcNow;
            var cachedMessages = messages.Select(m => converter.ToCachedMessage(m, utcNow)).ToList();
            cache.Add(cachedMessages, utcNow);

            // 回读每条消息，并断言其有效负载与写入的内容完全相同。
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(startSequenceNumber));
            for (int i = 0; i < messageCount; i++)
            {
                Assert.True(cache.TryGetNextMessage(cursor, out var batch), $"Missing message at index {i}");
                var container = Assert.IsType<TestBatchContainer>(batch);
                Assert.Equal(startSequenceNumber + i, container.SequenceToken.SequenceNumber);
                Assert.Equal(CreatePayload(i), container.Data);
            }
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            static byte[] CreatePayload(int index)
            {
                var payload = new byte[MessageSize];
                // 使用取决于索引的不同模式，以便检测任何跨消息的覆盖。
                for (int j = 0; j < payload.Length; j++)
                {
                    payload[j] = (byte)((index + j) & 0xFF);
                }
                return payload;
            }
        }

        /// <summary>
        /// 大小超过池化缓冲区容量的消息必须在不泄漏缓冲区的情况下失败。
        /// 失败的分配必须将缓冲块归还给池，使反复出现的超大（毒性）
        /// 消息能够复用同一个缓冲块，而不是导致保留的缓冲块集合无限增长。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void OversizedMessageDoesNotLeakBuffers()
        {
            int buffersCreated = 0;
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() =>
            {
                buffersCreated++;
                return new FixedSizeBuffer(PooledBufferSize);
            });
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            // 大于整个缓冲区的有效负载不可能编码到单个片段中。
            var oversized = new TestQueueMessage
            {
                StreamId = stream,
                SequenceNumber = 1,
                Data = new byte[PooledBufferSize * 2],
            };

            for (int i = 0; i < 5; i++)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => converter.ToCachedMessage(oversized, DateTime.UtcNow));
            }

            // 每次失败尝试均将缓冲块归还给池，因此始终只
            // 创建并复用一个缓冲块。修复前每次尝试都泄漏一个缓冲块（buffersCreated == 5）。
            Assert.Equal(1, buffersCreated);
        }

        private static (PooledQueueCache Cache, CachedMessageConverter Converter) CreateCache()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(
                NullLogger.Instance,
                new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)),
                null,
                null)
            {
                PurgeObservable = cache,
            };
            return (cache, new CachedMessageConverter(bufferPool, evictionStrategy));
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void AvoidCacheMissNotEmptyCache()
        {
            AvoidCacheMiss(false);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void AvoidCacheMissEmptyCache()
        {
            AvoidCacheMiss(true);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void RefreshIdleCursorStartsAtProvidedTokenAfterPurge()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            // 禁用清除元数据，以重现游标的生命周期同时超出缓存数据及其令牌元数据生命周期的情况。
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());

            AddMessage(1);
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(1));
            Assert.True(cache.TryGetNextMessage(cursor, out _));
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            cache.RemoveOldestMessage();
            AddMessage(2);
            var newEventToken = new EventSequenceTokenV2(2);

            cache.Refresh(cursor, newEventToken);

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(newEventToken, message.SequenceToken);
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            void AddMessage(long sequenceNumber)
            {
                var now = DateTime.UtcNow;
                var message = new TestQueueMessage
                {
                    StreamId = stream,
                    SequenceNumber = sequenceNumber,
                };
                cache.Add([converter.ToCachedMessage(message, now)], now);
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void RefreshDoesNotRepositionActiveCursor()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 1 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = stream, SequenceNumber = 2 }, now),
            ], now);
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(1));

            cache.Refresh(cursor, new EventSequenceTokenV2(2));

            Assert.True(cache.TryGetNextMessage(cursor, out var firstMessage));
            Assert.Equal(1, firstMessage.SequenceToken.SequenceNumber);
            Assert.True(cache.TryGetNextMessage(cursor, out var secondMessage));
            Assert.Equal(2, secondMessage.SequenceToken.SequenceNumber);
            Assert.False(cache.TryGetNextMessage(cursor, out _));
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void RefreshDoesNotRewindCursorHoldingFutureToken()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());

            // 将尚未定位的游标定位到相对于缓存仍处于未来的令牌。
            AddMessage(1);
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(5));

            // 使用较旧的令牌刷新时，不得将游标回退到其请求的起始位置之前。
            cache.Refresh(cursor, new EventSequenceTokenV2(2));

            AddMessage(2);
            AddMessage(3);
            AddMessage(4);
            AddMessage(5);

            Assert.True(cache.TryGetNextMessage(cursor, out var message));
            Assert.Equal(5, message.SequenceToken.SequenceNumber);
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            void AddMessage(long sequenceNumber)
            {
                var now = DateTime.UtcNow;
                var message = new TestQueueMessage
                {
                    StreamId = stream,
                    SequenceNumber = sequenceNumber,
                };
                cache.Add([converter.ToCachedMessage(message, now)], now);
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void AvoidCacheMissMultipleStreamsActive()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null, TimeSpan.FromSeconds(30));
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var seqNumber = 123;
            var streamKey = Guid.NewGuid();
            var stream = StreamId.Create(TestStreamNamespace, streamKey);

            // 为目标流将一条消息入队
            var firstSequenceNumber = EnqueueMessage(streamKey);

            // 为其他流将若干其他消息入队
            EnqueueMessage(Guid.NewGuid());
            EnqueueMessage(Guid.NewGuid());

            // 消费第一个事件，确认游标已移至最近看到的事件（该事件与目标 streamIdentity 不匹配）
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(firstSequenceNumber));
            Assert.True(cache.TryGetNextMessage(cursor, out var firstContainer));
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            // 移除多个事件，包括游标当前指向的事件
            cache.RemoveOldestMessage();
            cache.RemoveOldestMessage();
            cache.RemoveOldestMessage();

            // 为该流将另一条消息入队
            var lastSequenceNumber = EnqueueMessage(streamKey);

            // 应能够消费刚刚推入的事件
            Assert.True(cache.TryGetNextMessage(cursor, out var lastContainer));
            Assert.Equal(stream, lastContainer.StreamId);
            Assert.Equal(lastSequenceNumber, lastContainer.SequenceToken.SequenceNumber);

            long EnqueueMessage(Guid streamId)
            {
                var now = DateTime.UtcNow;
                var msg = new TestQueueMessage
                {
                    StreamId = StreamId.Create(TestStreamNamespace, streamId),
                    SequenceNumber = seqNumber,
                };
                cache.Add(new List<CachedMessage>() { converter.ToCachedMessage(msg, now) }, now);
                seqNumber++;
                return msg.SequenceNumber;
            }
        }

        private void AvoidCacheMiss(bool emptyCache)
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null, TimeSpan.FromSeconds(30));
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var seqNumber = 123;
            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());

            // 为该流将一条消息入队
            var firstSequenceNumber = EnqueueMessage(stream);

            // 消费第一个事件
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(firstSequenceNumber));
            Assert.True(cache.TryGetNextMessage(cursor, out var firstContainer));
            Assert.Equal(stream, firstContainer.StreamId);
            Assert.Equal(firstSequenceNumber, firstContainer.SequenceToken.SequenceNumber);

            // 移除已消费的第一条消息
            cache.RemoveOldestMessage();

            if (!emptyCache)
            {
                // 将与该流无关的消息入队，
                // 使缓存不为空
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
            }

            // 为该流将另一条消息入队
            var lastSequenceNumber = EnqueueMessage(stream);

            // 应能够消费刚刚推入的事件
            Assert.True(cache.TryGetNextMessage(cursor, out var lastContainer));
            Assert.Equal(stream, lastContainer.StreamId);
            Assert.Equal(lastSequenceNumber, lastContainer.SequenceToken.SequenceNumber);

            long EnqueueMessage(StreamId streamId)
            {
                var now = DateTime.UtcNow;
                var msg = new TestQueueMessage
                {
                    StreamId = streamId,
                    SequenceNumber = seqNumber,
                };
                cache.Add(new List<CachedMessage>() { converter.ToCachedMessage(msg, now) }, now);
                seqNumber++;
                return msg.SequenceNumber;
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void SimpleCacheMiss()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null, TimeSpan.FromSeconds(10));
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var seqNumber = 123;
            var streamKey = Guid.NewGuid();
            var stream = StreamId.Create(TestStreamNamespace, streamKey);

            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(seqNumber));
            // 先为该流将一条消息入队，再将另一条发往其他流的消息入队
            EnqueueMessage(streamKey);
            EnqueueMessage(Guid.NewGuid());
            // 消费该流，应该能够正常进行
            Assert.True(cache.TryGetNextMessage(cursor, out _));
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            // 将新批次入队
            // 第一条和最后一条消息发往该流，其他消息
            // 发往其他流
            EnqueueMessage(streamKey);
            for (var idx = 0; idx < 20; idx++)
            {
                EnqueueMessage(Guid.NewGuid());
            }

            // 从缓存中移除前三条消息
            cache.RemoveOldestMessage(); // 发往该流，已消费
            cache.RemoveOldestMessage(); // 不发往该流
            cache.RemoveOldestMessage(); // 发往该流，未消费

            // 为该流将一条新消息入队
            EnqueueMessage(streamKey);

            var result = cache.TryGetNextMessageWithResult(cursor, out _);
            Assert.Equal(QueueCacheCursorMoveResultKind.CacheMiss, result.Kind);
            var cacheMiss = Assert.NotNull(result.CacheMiss);
            Assert.NotNull(cacheMiss.Requested);
            Assert.NotNull(cacheMiss.Low);
            Assert.NotNull(cacheMiss.High);

            // 为现有缓存实现保留旧版 API 的行为。
            Assert.Throws<QueueCacheMissException>(() => cache.TryGetNextMessage(cursor, out _));

            long EnqueueMessage(Guid streamId)
            {
                var now = DateTime.UtcNow;
                var msg = new TestQueueMessage
                {
                    StreamId = StreamId.Create(TestStreamNamespace, streamId),
                    SequenceNumber = seqNumber,
                };
                cache.Add(new List<CachedMessage>() { converter.ToCachedMessage(msg, now) }, now);
                seqNumber++;
                return msg.SequenceNumber;
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TryGetCursorReportsCacheMiss()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null, TimeSpan.FromSeconds(10));
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);
            var streamId = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            var message = new TestQueueMessage
            {
                StreamId = streamId,
                SequenceNumber = 2,
            };
            cache.Add([converter.ToCachedMessage(message, now)], now);

            var requestedToken = new EventSequenceTokenV2(1);
            var result = cache.TryGetCursor(streamId, requestedToken);

            Assert.Equal(QueueCacheCursorResultKind.CacheMiss, result.Kind);
            Assert.Null(result.Cursor);
            var cacheMiss = Assert.NotNull(result.CacheMiss);
            Assert.Same(requestedToken, cacheMiss.RequestedToken);
            Assert.Equal(message.SequenceNumber, cacheMiss.LowToken!.SequenceNumber);
            Assert.Equal(message.SequenceNumber, cacheMiss.HighToken!.SequenceNumber);
            Assert.Equal(requestedToken.ToString(), cacheMiss.Requested);
            var exception = Assert.Throws<QueueCacheMissException>(() => cache.GetCursor(streamId, requestedToken));
            Assert.Equal(cacheMiss.Requested, exception.Requested);
            Assert.Equal(cacheMiss.Low, exception.Low);
            Assert.Equal(cacheMiss.High, exception.High);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void TryGetCursorPreservesCacheMissWhenCacheMutatesAfterPreflight()
        {
            var (cache, converter) = CreateCache();
            var streamId = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var now = DateTime.UtcNow;
            cache.Add(
            [
                converter.ToCachedMessage(new TestQueueMessage { StreamId = streamId, SequenceNumber = 1 }, now),
                converter.ToCachedMessage(new TestQueueMessage { StreamId = streamId, SequenceNumber = 2 }, now),
            ], now);
            var requestedToken = new MutatingSequenceToken(1, 3, cache.RemoveOldestMessage);

            var result = cache.TryGetCursor(streamId, requestedToken);

            Assert.Equal(QueueCacheCursorResultKind.CacheMiss, result.Kind);
            Assert.Null(result.Cursor);
            var cacheMiss = Assert.NotNull(result.CacheMiss);
            Assert.Same(requestedToken, cacheMiss.RequestedToken);
            Assert.Equal(2, cacheMiss.LowToken!.SequenceNumber);
            Assert.Equal(2, cacheMiss.HighToken!.SequenceNumber);
        }

        /// <summary>
        /// 序列号间隔可能超过 int.MaxValue：MemoryStreamQueueGrain 每次激活时均以
        /// DateTime.UtcNow.Ticks 初始化其计数器，因此队列 Grain 停用再激活会使
        /// 编号一步大幅向前跳跃。Compare 不得将差值截断为 32 位。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void CompareHandlesSequenceNumberGapsLargerThanIntMaxValue()
        {
            // 选取使截断后的 32 位差值符号错误或为零的间隔。
            var olderMessage = new CachedMessage { SequenceNumber = 100 };
            Assert.True(olderMessage.Compare(new EventSequenceTokenV2(100L + int.MaxValue + 2)) < 0);
            Assert.True(olderMessage.Compare(new EventSequenceTokenV2(100 + (1L << 32))) < 0);

            var newerMessage = new CachedMessage { SequenceNumber = 100L + int.MaxValue + 2 };
            Assert.True(newerMessage.Compare(new EventSequenceTokenV2(100)) > 0);

            // 序列号相同时仍按事件索引比较。
            var message = new CachedMessage { SequenceNumber = 100, EventIndex = 1 };
            Assert.True(message.Compare(new EventSequenceTokenV2(100, 0)) > 0);
            Assert.True(message.Compare(new EventSequenceTokenV2(100, 2)) < 0);
            Assert.Equal(0, message.Compare(new EventSequenceTokenV2(100, 1)));
        }

        /// <summary>
        /// 空闲游标必须能够看到在序列号跳跃超过
        /// int.MaxValue 后发布的消息（例如队列 Grain 停用后重新以
        /// DateTime.UtcNow.Ticks 初始化其计数器）。比较操作溢出时，新消息看起来比
        /// 游标更旧，导致投递悄然停滞。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void IdleCursorContinuesAfterLargeSequenceNumberJump()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null);
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            const long firstEpochSequenceNumber = 1000;
            // 大于 int.MaxValue，选取此值使截断后的 32 位差值符号反转。
            const long secondEpochSequenceNumber = firstEpochSequenceNumber + 5 + (3L << 30);

            // 添加第一个世代的消息并全部消费，使游标处于空闲状态。
            for (var i = 0; i < 5; i++)
            {
                EnqueueMessage(firstEpochSequenceNumber + i);
            }
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(firstEpochSequenceNumber));
            for (var i = 0; i < 5; i++)
            {
                Assert.True(cache.TryGetNextMessage(cursor, out _));
            }
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            // 队列 Grain 重新激活，其编号大幅跳跃，远超前一个世代。
            EnqueueMessage(secondEpochSequenceNumber);

            // 空闲游标应获取到新消息。
            Assert.True(cache.TryGetNextMessage(cursor, out var container));
            Assert.Equal(secondEpochSequenceNumber, container.SequenceToken.SequenceNumber);

            void EnqueueMessage(long sequenceNumber)
            {
                var now = DateTime.UtcNow;
                var msg = new TestQueueMessage
                {
                    StreamId = stream,
                    SequenceNumber = sequenceNumber,
                };
                cache.Add(new List<CachedMessage>() { converter.ToCachedMessage(msg, now) }, now);
            }
        }

        /// <summary>
        /// 定位到序列号跳跃超过 int.MaxValue 之后的令牌的游标
        /// 必须从该令牌开始。比较操作溢出时，该令牌看起来比最旧的
        /// 缓存消息还旧；由于存在清除元数据，游标会被悄然回退到
        /// 最旧的缓存消息，从而重新投递整个缓存中的消息。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void CursorNotRewoundToOldestMessageAfterLargeSequenceNumberJump()
        {
            var bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(PooledBufferSize));
            var dataAdapter = new TestCacheDataAdapter();
            var cache = new PooledQueueCache(dataAdapter, NullLogger.Instance, null, null, TimeSpan.FromSeconds(30));
            var evictionStrategy = new ChronologicalEvictionStrategy(NullLogger.Instance, new TimePurgePredicate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), null, null);
            evictionStrategy.PurgeObservable = cache;
            var converter = new CachedMessageConverter(bufferPool, evictionStrategy);

            var stream = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            const long firstEpochSequenceNumber = 1000;
            // 大于 int.MaxValue，选取此值使截断后的 32 位差值符号反转。
            const long secondEpochSequenceNumber = firstEpochSequenceNumber + 1 + int.MaxValue + 3L;

            // 添加第一个世代的消息。
            for (var i = 0; i < 5; i++)
            {
                EnqueueMessage(firstEpochSequenceNumber + i);
            }

            // 逐出最旧的消息，以便记录该流的清除元数据。
            cache.RemoveOldestMessage();

            // 队列 Grain 重新激活，其编号大幅跳跃，远超前一个世代。
            EnqueueMessage(secondEpochSequenceNumber);

            // 针对新消息的游标必须仅投递该消息，而不是整个缓存中的消息。
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(secondEpochSequenceNumber));
            Assert.True(cache.TryGetNextMessage(cursor, out var container));
            Assert.Equal(secondEpochSequenceNumber, container.SequenceToken.SequenceNumber);
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            void EnqueueMessage(long sequenceNumber)
            {
                var now = DateTime.UtcNow;
                var msg = new TestQueueMessage
                {
                    StreamId = stream,
                    SequenceNumber = sequenceNumber,
                };
                cache.Add(new List<CachedMessage>() { converter.ToCachedMessage(msg, now) }, now);
            }
        }

        private int RunGoldenPath(PooledQueueCache cache, CachedMessageConverter converter, int startOfCache)
        {
            int sequenceNumber = startOfCache;
            IBatchContainer? batch;

            var stream1 = StreamId.Create(TestStreamNamespace, Guid.NewGuid());
            var stream2 = StreamId.Create(TestStreamNamespace, Guid.NewGuid());

            // 现在将比游标更新的消息添加到缓存
            // 添加足够多的消息以填满池
            List<TestQueueMessage> messages = Enumerable.Range(0, MessagesPerBuffer * PooledBufferCount)
                .Select(i => new TestQueueMessage
                {
                    StreamId = i % 2 == 0 ? stream1 : stream2,
                    SequenceNumber = sequenceNumber + i
                })
                .ToList();
            DateTime utcNow = DateTime.UtcNow;
            List<CachedMessage> cachedMessages = messages
                .Select(m => converter.ToCachedMessage(m, utcNow))
                .ToList();
            cache.Add(cachedMessages, utcNow);
            sequenceNumber += MessagesPerBuffer * PooledBufferCount;

            // 获取 stream1 的游标，使用该游标遍历流中的所有事件
            object stream1Cursor = cache.GetCursor(stream1, new EventSequenceTokenV2(startOfCache));
            int stream1EventCount = 0;
            while (cache.TryGetNextMessage(stream1Cursor, out batch))
            {
                Assert.NotNull(stream1Cursor);
                Assert.NotNull(batch);
                Assert.Equal(stream1, batch.StreamId);
                Assert.NotNull(batch.SequenceToken);
                stream1EventCount++;
            }
            Assert.Equal((sequenceNumber - startOfCache) / 2, stream1EventCount);

            // 获取 stream2 的游标，使用该游标遍历流中的所有事件
            object stream2Cursor = cache.GetCursor(stream2, new EventSequenceTokenV2(startOfCache));
            int stream2EventCount = 0;
            while (cache.TryGetNextMessage(stream2Cursor, out batch))
            {
                Assert.NotNull(stream2Cursor);
                Assert.NotNull(batch);
                Assert.Equal(stream2, batch.StreamId);
                Assert.NotNull(batch.SequenceToken);
                stream2EventCount++;
            }
            Assert.Equal((sequenceNumber - startOfCache) / 2, stream2EventCount);

            // 向缓存添加一个缓冲块容量的事件，再遍历每个游标。重复足够多次，以使缓存被填满两次。
            for (int j = 0; j < PooledBufferCount * 2; j++)
            {
                List<TestQueueMessage> moreMessages = Enumerable.Range(0, MessagesPerBuffer)
                .Select(i => new TestQueueMessage
                {
                    StreamId = i % 2 == 0 ? stream1 : stream2,
                    SequenceNumber = sequenceNumber + i
                })
                .ToList();
                utcNow = DateTime.UtcNow;
                List<CachedMessage> moreCachedMessages = moreMessages
                    .Select(m => converter.ToCachedMessage(m, utcNow))
                    .ToList();
                cache.Add(moreCachedMessages, utcNow);
                sequenceNumber += MessagesPerBuffer;

                // 使用游标遍历流中的所有事件
                while (cache.TryGetNextMessage(stream1Cursor, out batch))
                {
                    Assert.NotNull(stream1Cursor);
                    Assert.NotNull(batch);
                    Assert.Equal(stream1, batch.StreamId);
                    Assert.NotNull(batch.SequenceToken);
                    stream1EventCount++;
                }
                Assert.Equal((sequenceNumber - startOfCache) / 2, stream1EventCount);

                // 使用游标遍历流中的所有事件
                while (cache.TryGetNextMessage(stream2Cursor, out batch))
                {
                    Assert.NotNull(stream2Cursor);
                    Assert.NotNull(batch);
                    Assert.Equal(stream2, batch.StreamId);
                    Assert.NotNull(batch.SequenceToken);
                    stream2EventCount++;
                }
                Assert.Equal((sequenceNumber - startOfCache) / 2, stream2EventCount);
            }
            return sequenceNumber;
        }
    }
}

#pragma warning restore CS0618
