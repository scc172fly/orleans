using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using Xunit;

#pragma warning disable CS0618 // This suite verifies compatibility of the legacy cursor APIs alongside their replacements.

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
                //降低有效载荷的序列化
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

                // 获取当前块的段
                ArraySegment<byte> segment;
                if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
                {
                    // 没有块或整块, 获得新块并再次试
                    var newBuffer = bufferPool.Allocate();
                    // 如果此失败与干净块块失败, 那么请求的大小太大; 返回
                    // 现金池的未用块块,但失败了,而不是漏出一个从未承诺的块。
                    if (!newBuffer.TryGetSegment(size, out segment))
                    {
                        newBuffer.Dispose();
                        string errmsg = string.Format(CultureInfo.InvariantCulture,
                            "Message size is too big. MessageSize: {0}", size);
                        throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                    }
                    currentBuffer = newBuffer;
                    //[调用 EvictionStrategy的 OnBlockAllocated 方法
                    this.evictionStrategy.OnBlockAllocated(currentBuffer);
                }
                // 编码名称空间、 偏移、 分割键、 属性和有效载荷等元件
                int writeOffset = 0;
                SegmentBuilder.Append(segment, ref writeOffset, queueMessage.Data);
                return segment;
            }
        }

        /// <summary>
        /// 填充缓存中两个流。
        /// 获取有效的光标以启动每个流 。
        /// 每个光标走过每个光标,直到没有关于每一流的数据。
        /// 将信件添加到缓存和行走光标的替代信息 。
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
        /// 运行正常的金色路径测试, 然后清理缓存, 然后再运行另一个金色路径测试 。  
        /// 目标是确保当所有数据被清理时, 缓存会得到正确清除 。
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
        /// https://github.com/dotnet/orleans/issues/10263的回归测试。
        /// 批次在被装入缓冲器之前,先编码成集合缓冲器。
        /// 批量横跨多个 <see cref="FixedSizeBuffer"/> ,而缓存为空,
        /// 驱逐战略不得再利用仍然维持在飞行中的缓冲带(未承付)
        /// 此前曾有:缓冲剂还给了LIFO
        /// 在同一批下一批的下一个分配款 直接倒过来,并翻了
        /// 先前从 mof 0 上发出的信息, 静悄悄地腐蚀其有效载荷( 稍后作为
        /// ArgumentOutOfRangeException 的同时解码。 本测试写有不同的有效载荷和
        /// 声称每个人都能活下来 加上一个空缓存
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
            // 足够在一组内覆盖多个缓冲线的讯息。
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

            // 将整批货物编码为集合缓冲器,然后承诺 - 与实际的顺序相同
            // EventHub/记忆/发电机缓存手段的使用以及触发腐败的命令。
            var utcNow = DateTime.UtcNow;
            var cachedMessages = messages.Select(m => converter.ToCachedMessage(m, utcNow)).ToList();
            cache.Add(cachedMessages, utcNow);

            // 读回每条信息 并声明它的载荷 正是我们写的东西。
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
                // 不同的,依指数为主的模式,因此任何交叉信息覆盖符都可以被检测到。
                for (int j = 0; j < payload.Length; j++)
                {
                    payload[j] = (byte)((index + j) & 0xFF);
                }
                return payload;
            }
        }

        /// <summary>
        /// 无法永远融入集合缓冲的过于大的信息必须失败,而不泄露缓冲。
        /// 无法分配的区块必须返回到重复过大尺寸( poison) 的池内
        /// 信息再利用一个区块,而不是增加保留部分,不设约束。
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
            // 大于整个缓冲的有效载荷永远无法编码成一个单一的段。
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

            // 每次尝试失败都返回池塘, 所以只有一个街区是永远
            // 在修复之前,每次尝试都漏出一个块(buffersCreated = 5)。
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
            // 禁用清理元数据复制光标比缓存数据及其象征性元数据活得快。
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

            // 将一个未设定的光标定位在未来仍然与缓存相对的标记上。
            AddMessage(1);
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(5));

            // 以旧的标记刷新时, 不能在光标请求启动后倒回光标 。
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

            // 发送一条消息给流
            var firstSequenceNumber = EnqueueMessage(streamKey);

            // 为其它串流发送一些其他消息
            EnqueueMessage(Guid.NewGuid());
            EnqueueMessage(Guid.NewGuid());

            // 将第一个事件合并为第一个事件, 并看到光标已移动到最后一次显示的事件( 与我们的 streamIdentity不匹配)
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(firstSequenceNumber));
            Assert.True(cache.TryGetNextMessage(cursor, out var firstContainer));
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            // 删除多个事件, 包括光标当前指向的事件
            cache.RemoveOldestMessage();
            cache.RemoveOldestMessage();
            cache.RemoveOldestMessage();

            // 给流发送新信件
            var lastSequenceNumber = EnqueueMessage(streamKey);

            // 能够消耗刚刚推出的这个事件
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

            // 给流发送消息
            var firstSequenceNumber = EnqueueMessage(stream);

            // 包含首个事件
            var cursor = cache.GetCursor(stream, new EventSequenceTokenV2(firstSequenceNumber));
            Assert.True(cache.TryGetNextMessage(cursor, out var firstContainer));
            Assert.Equal(stream, firstContainer.StreamId);
            Assert.Equal(firstSequenceNumber, firstContainer.SequenceToken.SequenceNumber);

            // 删除已消耗的第一则信件
            cache.RemoveOldestMessage();

            if (!emptyCache)
            {
                // 添加与流流无关的内容
                // 所以缓存不是空的
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
                EnqueueMessage(StreamId.Create(TestStreamNamespace, Guid.NewGuid()));
            }

            // 给流发送新信件
            var lastSequenceNumber = EnqueueMessage(stream);

            // 能够消耗刚刚推出的这个事件
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
            // 开始先排成流信息排队, 然后排在布后面, 下一个排在另一个列成另一行
            EnqueueMessage(streamKey);
            EnqueueMessage(Guid.NewGuid());
            // 混成流,应该没事
            Assert.True(cache.TryGetNextMessage(cursor, out _));
            Assert.False(cache.TryGetNextMessage(cursor, out _));

            // 加入新批次
            // 发送到流的首条和最后一条消息, 跟随电文
            // 指定用于其他流
            EnqueueMessage(streamKey);
            for (var idx = 0; idx < 20; idx++)
            {
                EnqueueMessage(Guid.NewGuid());
            }

            // 从缓存中删除前三封信件
            cache.RemoveOldestMessage(); // 最终成为溪流,消耗
            cache.RemoveOldestMessage(); // 并非预定流
            cache.RemoveOldestMessage(); // 最终成为溪流,未消耗

            // 给流发送新消息
            EnqueueMessage(streamKey);

            var result = cache.TryGetNextMessageWithResult(cursor, out _);
            Assert.Equal(QueueCacheCursorMoveResultKind.CacheMiss, result.Kind);
            var cacheMiss = Assert.NotNull(result.CacheMiss);
            Assert.NotNull(cacheMiss.Requested);
            Assert.NotNull(cacheMiss.Low);
            Assert.NotNull(cacheMiss.High);

            // 保存现有缓存执行的遗留 API 行为。
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
        /// 序列数差距可能超过 int.MaxValue: MemoryStreamQueueGrain 种子的抗
        /// 与 DateTime.UtcNow.Ticks 每次启动时都排队 grain 停止作用
        /// 以单步前进的远处编号。 比较不能缩短差数为 32 比特 。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void CompareHandlesSequenceNumberGapsLargerThanIntMaxValue()
        {
            // 选中的空白, 短短的 32 比特差有错误的符号或为零 。
            var olderMessage = new CachedMessage { SequenceNumber = 100 };
            Assert.True(olderMessage.Compare(new EventSequenceTokenV2(100L + int.MaxValue + 2)) < 0);
            Assert.True(olderMessage.Compare(new EventSequenceTokenV2(100 + (1L << 32))) < 0);

            var newerMessage = new CachedMessage { SequenceNumber = 100L + int.MaxValue + 2 };
            Assert.True(newerMessage.Compare(new EventSequenceTokenV2(100)) > 0);

            // 相同的序列数仍然按事件指数进行比较。
            var message = new CachedMessage { SequenceNumber = 100, EventIndex = 1 };
            Assert.True(message.Compare(new EventSequenceTokenV2(100, 0)) > 0);
            Assert.True(message.Compare(new EventSequenceTokenV2(100, 2)) < 0);
            Assert.Equal(0, message.Compare(new EventSequenceTokenV2(100, 1)));
        }

        /// <summary>
        /// 空闲光标光标必须看到序列数跳过大于
        /// int.MaxValue (e.g).队队 grain 被停用,并重新播种其计数器
        /// DateTime.UtcNow.Ticks。在对新信件的溢出比较后,新信件看起来比
        /// 光标和投递时静悄悄地抛锚。
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
            // 大于 int.MaxValue, 选中的, 所以短短的 32 比特差的翻转符号 。
            const long secondEpochSequenceNumber = firstEpochSequenceNumber + 5 + (3L << 30);

            // 添加第一个时代的信息, 并全部消耗, 让光标空闲 。
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

            // 队列 grain 已恢复, 其编号跳跃远早于前一个时代 。
            EnqueueMessage(secondEpochSequenceNumber);

            // 闲置光标应该会接收新消息 。
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
        /// 光标放置在一个符号上, 其序列数跳跃大于 int.MaxValue后坐着的符号
        /// 必须在该标记上开始。 如果对牌进行溢出比较, 该标记看起来比最老的要长
        /// 缓存信件; 因为存在清除元数据, 那么光标会默默地重写到
        /// 最老的缓存信件和整个缓存已重新发送 。
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
            // 大于 int.MaxValue, 选中的, 所以短短的 32 比特差的翻转符号 。
            const long secondEpochSequenceNumber = firstEpochSequenceNumber + 1 + int.MaxValue + 3L;

            // 添加第一个时代的信息 。
            for (var i = 0; i < 5; i++)
            {
                EnqueueMessage(firstEpochSequenceNumber + i);
            }

            // 将最古老的信息从电流中录入净化元数据
            cache.RemoveOldestMessage();

            // 队列 grain 已恢复, 其编号跳跃远早于前一个时代 。
            EnqueueMessage(secondEpochSequenceNumber);

            // 新信件的光标必须只发送该信件, 而不是整个缓存 。
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

            // 现在将信件添加到比光标更新的缓存中
            // 添加足够填充池
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

            // 获取 slow 1 的光标, 以光标在流中行走所有事件
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

            // 获取 slob 2 的光标, 使用光标在流中所有事件都行走
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

            // 将一个值的事件块添加到缓存中, 然后每个光标都行走。 做这个足够多的时间来填充缓存两次 。
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

                // 使用光标在溪流中所有事件行走
                while (cache.TryGetNextMessage(stream1Cursor, out batch))
                {
                    Assert.NotNull(stream1Cursor);
                    Assert.NotNull(batch);
                    Assert.Equal(stream1, batch.StreamId);
                    Assert.NotNull(batch.SequenceToken);
                    stream1EventCount++;
                }
                Assert.Equal((sequenceNumber - startOfCache) / 2, stream1EventCount);

                // 使用光标在溪流中所有事件行走
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
