using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    internal class CacheBucket
    {
        // 为检测背压，我们维护一个包含 10 个桶的直方图。
        // 每个桶记录缓存中属于该桶的数据项数量，
        // 以及指向该桶内数据项的游标数量。
        // 添加和移除缓存数据项时更新 NumCurrentItems（可能创建或移除桶）。
        // 每次移动游标时更新 NumCurrentCursors。
        // 在缓存已满的情况下，如果第一个（最旧的）桶至少有一个游标指向其中的数据项，则认为存在背压。
        internal int NumCurrentItems { get; private set; }
        internal int NumCurrentCursors { get; private set; }

        internal void UpdateNumItems(int val)
        {
            NumCurrentItems = NumCurrentItems + val;
        }
        internal void UpdateNumCursors(int val)
        {
            NumCurrentCursors = NumCurrentCursors + val;
        }
    }

    internal class SimpleQueueCacheItem
    {
        internal IBatchContainer Batch = null!; // 始终在构造完成后立即通过对象初始化器设置。
        internal bool DeliveryFailure;
        internal StreamSequenceToken SequenceToken = null!; // 始终在构造完成后立即通过对象初始化器设置。
        internal CacheBucket CacheBucket = null!; // 始终在构造完成后立即通过对象初始化器设置。
    }

    /// <summary>
    /// 将数据项保留在内存中的队列缓存。
    /// </summary>
    public partial class SimpleQueueCache : IQueueCache
    {
        private readonly LinkedList<SimpleQueueCacheItem> cachedMessages;
        private readonly int maxCacheSize;
        private readonly ILogger logger;
        private readonly List<CacheBucket> cacheCursorHistogram; // 用于背压检测。
        private readonly Func<SimpleQueueCache, StreamId, SimpleQueueCacheCursor>? cursorFactory;
        private const int NUM_CACHE_HISTOGRAM_BUCKETS = 10;
        private readonly int CACHE_HISTOGRAM_MAX_BUCKET_SIZE;

        /// <summary>
        /// 获取缓存中的数据项数量。
        /// </summary>
        public int Size => cachedMessages.Count;

        /// <inheritdoc />
        public int GetMaxAddCount()
        {
            return CACHE_HISTOGRAM_MAX_BUCKET_SIZE;
        }

        /// <summary>
        /// 初始化 <see cref="SimpleQueueCache"/> 类的新实例。
        /// </summary>
        /// <param name="cacheSize">缓存大小。</param>
        /// <param name="logger">日志记录器。</param>
        public SimpleQueueCache(int cacheSize, ILogger logger)
            : this(cacheSize, logger, cursorFactory: null)
        {
        }

        // 允许测试观察游标所有权，而无需公开工厂扩展点。
        internal SimpleQueueCache(
            int cacheSize,
            ILogger logger,
            Func<SimpleQueueCache, StreamId, SimpleQueueCacheCursor>? cursorFactory)
        {
            cachedMessages = new LinkedList<SimpleQueueCacheItem>();
            maxCacheSize = cacheSize;

            this.logger = logger;
            this.cursorFactory = cursorFactory;
            cacheCursorHistogram = new List<CacheBucket>();
            CACHE_HISTOGRAM_MAX_BUCKET_SIZE = Math.Max(cacheSize / NUM_CACHE_HISTOGRAM_BUCKETS, 1); // 共有 10 个桶。
        }

        /// <inheritdoc />
        public virtual bool IsUnderPressure()
        {
            return cacheCursorHistogram.Count >= NUM_CACHE_HISTOGRAM_BUCKETS;
        }


        /// <inheritdoc />
        public virtual bool TryPurgeFromCache([MaybeNullWhen(false)] out IList<IBatchContainer> purgedItems)
        {
            purgedItems = null;
            if (cachedMessages.Count == 0) return false; // 缓存为空。
            if (cacheCursorHistogram.Count == 0) return false;  // 尚无游标，基本上还没有消费者。
            if (cacheCursorHistogram[0].NumCurrentCursors > 0) return false; // 最旧的桶仍有活动消费者：快速路径。

            var allItems = new List<IBatchContainer>();
            while (cacheCursorHistogram.Count > 0 && cacheCursorHistogram[0].NumCurrentCursors == 0)
            {
                List<IBatchContainer> items = DrainBucket(cacheCursorHistogram[0]);
                allItems.AddRange(items);
                cacheCursorHistogram.RemoveAt(0); // 移除最后一个桶。
            }
            purgedItems = allItems;

            LogDebugTryPurgeFromCache(allItems.Count);

            return true;
        }

        private List<IBatchContainer> DrainBucket(CacheBucket bucket)
        {
            var itemsToRelease = new List<IBatchContainer>(bucket.NumCurrentItems);
            // 从最后一个数据项开始遍历缓存中的所有数据项，
            // 从缓存中移除属于指定桶的数据项，直到进入下一个桶。
            while (bucket.NumCurrentItems > 0)
            {
                SimpleQueueCacheItem item = cachedMessages.Last!.Value; // bucket.NumCurrentItems > 0 表明列表非空。
                if (item.CacheBucket.Equals(bucket))
                {
                    if (!item.DeliveryFailure)
                    {
                        itemsToRelease.Add(item.Batch);
                    }
                    bucket.UpdateNumItems(-1);
                    cachedMessages.RemoveLast();
                }
                else
                {
                    // 此数据项已指向下一个桶，因此停止。
                    break;
                }
            }
            return itemsToRelease;
        }

        /// <inheritdoc />
        public virtual void AddToCache(IList<IBatchContainer> msgs)
        {
            if (msgs == null) throw new ArgumentNullException(nameof(msgs));

            LogDebugAddToCache(msgs.Count);
            foreach (var message in msgs)
            {
                Add(message, message.SequenceToken);
            }
        }

        /// <inheritdoc />
        /// <exception cref="QueueCacheMissException">
        /// 请求的序列令牌对应的位置早于缓存中最早保留的消息。
        /// </exception>
        [Obsolete("Use IQueueCache.TryGetCacheCursor instead.")]
        public virtual IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken? token)
        {
            if (TryGetCacheMiss(token, out var cacheMiss))
            {
                throw cacheMiss.ToException();
            }

            SimpleQueueCacheCursor? cursor = CreateCursor(streamId);
            try
            {
                if (InitializeCursor(cursor, token) is { } racedCacheMiss)
                {
                    throw racedCacheMiss.ToException();
                }

                var result = cursor;
                cursor = null;
                return result;
            }
            finally
            {
                cursor?.Dispose();
            }
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursor(
            StreamId streamId,
            StreamSequenceToken? token)
        {
            if (GetType() != typeof(SimpleQueueCache))
            {
                try
                {
#pragma warning disable CS0618 // 为兼容重写旧版方法的派生缓存而保留。
                    return QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(GetCacheCursor(streamId, token));
#pragma warning restore CS0618
                }
                catch (QueueCacheMissException exception)
                {
                    return QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(
                        new(exception.Requested, exception.Low, exception.High));
                }
            }

            if (TryGetCacheMiss(token, out var cacheMiss))
            {
                return QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(cacheMiss);
            }

            SimpleQueueCacheCursor? cursor = CreateCursor(streamId);
            try
            {
                if (InitializeCursor(cursor, token) is { } racedCacheMiss)
                {
                    return QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(racedCacheMiss);
                }

                var result = QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(cursor);
                cursor = null;
                return result;
            }
            finally
            {
                cursor?.Dispose();
            }
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                var result = ((IQueueCache)this).TryGetCacheCursor(streamId, null);
                if (result.Kind != QueueCacheCursorResultKind.Success)
                {
                    return result;
                }

                var latestCursor = result.Cursor!;
                var retainCursor = false;
                try
                {
                    var moveResult = latestCursor.MoveNextWithResult();
                    if (moveResult.CacheMiss is { } cacheMiss)
                    {
                        return QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(cacheMiss);
                    }

                    if (moveResult.Kind is not QueueCacheCursorMoveResultKind.Success and not QueueCacheCursorMoveResultKind.NoData)
                    {
                        throw new InvalidOperationException("The cursor move result is not initialized.");
                    }

                    retainCursor = true;
                    return result;
                }
                finally
                {
                    if (!retainCursor)
                    {
                        latestCursor.Dispose();
                    }
                }
            }

            if (startPosition != StreamSubscriptionStartPosition.EarliestAvailable)
            {
                throw new ArgumentOutOfRangeException(nameof(startPosition), startPosition, "The subscription start position is not defined.");
            }

            SimpleQueueCacheCursor? cursor = CreateCursor(streamId);
            try
            {
                InitializeCursorAtEarliestAvailable(cursor);
                var result = QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(cursor);
                cursor = null;
                return result;
            }
            finally
            {
                cursor?.Dispose();
            }
        }

        private SimpleQueueCacheCursor CreateCursor(StreamId streamId)
            => cursorFactory is { } factory ? factory(this, streamId) : new(this, streamId, logger);

        private void InitializeCursorAtEarliestAvailable(SimpleQueueCacheCursor cursor)
        {
            for (var node = cachedMessages.Last; node is not null; node = node.Previous)
            {
                if (cursor.IsInStream(node.Value.Batch))
                {
                    cursor.WaitingForEarliestAvailable = false;
                    SetCursor(cursor, node);
                    return;
                }
            }

            UnsetCursor(cursor, null);
            cursor.WaitingForEarliestAvailable = true;
        }

        internal QueueCacheMissInfo? InitializeCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? sequenceToken)
        {
            LogDebugInitializeCursor(cursor, sequenceToken);

            // 缓存中没有数据，清除令牌设置并等待更多数据。（译注：实现清除游标的元素位置，并保留传入的序列令牌。）
            if (cachedMessages.Count == 0)
            {
                UnsetCursor(cursor, sequenceToken);
                return null;
            }

            // 如果未提供令牌，则将令牌设置为缓存末端数据项对应的令牌。
            sequenceToken = sequenceToken ?? cachedMessages.First?.Value?.SequenceToken!; // 此处 cachedMessages.Count > 0（上文已检查），因此 First、Value 和 SequenceToken 保证不为 null。

            // 如果 sequenceToken 太新而尚未进入缓存，则清除令牌设置并等待更多数据。（译注：实现清除游标的元素位置，并保留该序列令牌。）
            if (EventSequenceTokenCompatibility.Compare(sequenceToken, cachedMessages.First!.Value.SequenceToken) > 0) // 此处 cachedMessages.Count > 0（上文已检查），因此 First 保证不为 null。
            {
                UnsetCursor(cursor, sequenceToken);
                return null;
            }

            LinkedListNode<SimpleQueueCacheItem> lastMessage = cachedMessages.Last!; // 此处 cachedMessages.Count > 0（上文已检查），因此 Last 保证不为 null。
            // 检查偏移量是否过旧，已不在缓存中。
            if (EventSequenceTokenCompatibility.Compare(sequenceToken, lastMessage.Value.SequenceToken) < 0)
            {
                return CreateCacheMissInfo(sequenceToken);
            }

            // 此时，请求的 sequenceToken 已设置且位于缓存的范围内。

            // 查找第一条偏移量小于或等于指定偏移量的消息。
            // 事件按从新到旧的顺序排列，因此从列表开头迭代，直到遇到偏移量更早的节点或到达列表末尾。
            LinkedListNode<SimpleQueueCacheItem>? node = cachedMessages.First;
            while (node != null && EventSequenceTokenCompatibility.Compare(node.Value.SequenceToken, sequenceToken) > 0)
            {
                // 是否已到达末尾？
                if (node.Next == null) // node 是最后一条消息。
                    break;

                // 如果 sequenceId 介于两者之间，则取较大的一个。
                if (EventSequenceTokenCompatibility.Compare(node.Next.Value.SequenceToken, sequenceToken) < 0)
                    break;

                node = node.Next;
            }

            // 从起始位置返回游标。
            SetCursor(cursor, node!); // 保证不为 null：node 初始为 First（由于 Count > 0，因此非 null），并且只有在确认 node.Next 非 null 后才重新赋值。
            return null;
        }

        internal void RefreshCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? sequenceToken)
        {
            LogDebugRefreshCursor(cursor, sequenceToken);

            // 如果尚未设置，则设置。
            if (!cursor.IsSet)
            {
                if (cursor.WaitingForEarliestAvailable)
                {
                    InitializeCursorAtEarliestAvailable(cursor);
                    return;
                }

                var token = cursor.SequenceToken ?? sequenceToken;
                if (InitializeCursor(cursor, token) is { } cacheMiss)
                {
                    throw cacheMiss.ToException();
                }
            }
        }

        private bool TryGetCacheMiss(StreamSequenceToken? sequenceToken, out QueueCacheMissInfo cacheMiss)
        {
            if (sequenceToken is null
                || cachedMessages.Count == 0
                || EventSequenceTokenCompatibility.Compare(sequenceToken, cachedMessages.Last!.Value.SequenceToken) >= 0)
            {
                cacheMiss = default;
                return false;
            }

            cacheMiss = CreateCacheMissInfo(sequenceToken);
            return true;
        }

        private QueueCacheMissInfo CreateCacheMissInfo(StreamSequenceToken requestedToken)
            => new(requestedToken, cachedMessages.Last!.Value.SequenceToken, cachedMessages.First!.Value.SequenceToken);

        /// <summary>
        /// 获取指定游标处缓存中的下一条消息。
        /// </summary>
        /// <param name="cursor"></param>
        /// <param name="batch"></param>
        /// <returns></returns>
        internal bool TryGetNextMessage(SimpleQueueCacheCursor cursor, out IBatchContainer? batch)
        {
            LogDebugTryGetNextMessage(cursor);

            batch = null;
            if (!cursor.IsSet) return false;

            // 如果已到达缓存末端，则取消设置游标，并将偏移量向前移动一个位置。
            if (cursor.Element == cachedMessages.First)
            {
                UnsetCursor(cursor, null);
            }
            else // 前进到下一项：
            {
                AdvanceCursor(cursor, cursor.Element!.Previous!); // cursor.IsSet 为 true，因此 Element 不为 null；Element != First，因此它具有 Previous 节点。
            }

            batch = cursor.Element?.Value.Batch;
            return (batch != null);
        }

        private void AdvanceCursor(SimpleQueueCacheCursor cursor, LinkedListNode<SimpleQueueCacheItem> item)
        {
            LogDebugUpdateCursor(cursor, item.Value.Batch);

            cursor.Element!.Value.CacheBucket.UpdateNumCursors(-1); // 从前一个桶中移除；此处 cursor.Element 不为 null（参见调用方）。
            cursor.Set(item);
            cursor.Element!.Value.CacheBucket.UpdateNumCursors(1);  // 添加到下一个桶。
        }

        internal void SetCursor(SimpleQueueCacheCursor cursor, LinkedListNode<SimpleQueueCacheItem> item)
        {
            LogDebugSetCursor(cursor, item.Value.Batch);

            cursor.Set(item);
            cursor.Element!.Value.CacheBucket.UpdateNumCursors(1);  // 添加到下一个桶；上面的 cursor.Set(item) 保证 Element 不为 null。
        }

        internal void UnsetCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? token)
        {
            LogDebugUnsetCursor(cursor);

            if (cursor.IsSet)
            {
                cursor.Element!.Value.CacheBucket.UpdateNumCursors(-1); // cursor.IsSet 为 true，因此 Element 不为 null。
            }
            cursor.UnSet(token);
        }

        private void Add(IBatchContainer batch, StreamSequenceToken sequenceToken)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            // 理论上不应发生这种情况，但仍作防御性处理。
            if (Size >= maxCacheSize) throw new CacheFullException();

            CacheBucket cacheBucket;
            if (cacheCursorHistogram.Count == 0)
            {
                cacheBucket = new CacheBucket();
                cacheCursorHistogram.Add(cacheBucket);
            }
            else
            {
                cacheBucket = cacheCursorHistogram[cacheCursorHistogram.Count - 1]; // 最后一个。
            }

            if (cacheBucket.NumCurrentItems == CACHE_HISTOGRAM_MAX_BUCKET_SIZE) // 最后一个桶已满，创建新桶。
            {
                cacheBucket = new CacheBucket();
                cacheCursorHistogram.Add(cacheBucket);
            }

            // 将消息添加到链表。
            var item = new SimpleQueueCacheItem
            {
                Batch = batch,
                SequenceToken = sequenceToken,
                CacheBucket = cacheBucket
            };

            cachedMessages.AddFirst(new LinkedListNode<SimpleQueueCacheItem>(item));
            cacheBucket.UpdateNumItems(1);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryPurgeFromCache: purged {PurgedItemsCount} items from cache."
        )]
        private partial void LogDebugTryPurgeFromCache(int purgedItemsCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "AddToCache: added {ItemCount} items to cache."
        )]
        private partial void LogDebugAddToCache(int itemCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "InitializeCursor: {Cursor} to sequenceToken {SequenceToken}"
        )]
        private partial void LogDebugInitializeCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? sequenceToken);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "RefreshCursor: {RefreshCursor} to sequenceToken {SequenceToken}"
        )]
        private partial void LogDebugRefreshCursor(SimpleQueueCacheCursor refreshCursor, StreamSequenceToken? sequenceToken);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryGetNextMessage: {Cursor}"
        )]
        private partial void LogDebugTryGetNextMessage(SimpleQueueCacheCursor cursor);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "UpdateCursor: {Cursor} to item {Item}"
        )]
        private partial void LogDebugUpdateCursor(SimpleQueueCacheCursor cursor, IBatchContainer item);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SetCursor: {Cursor} to item {Item}"
        )]
        private partial void LogDebugSetCursor(SimpleQueueCacheCursor cursor, IBatchContainer item);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "UnsetCursor: {Cursor}"
        )]
        private partial void LogDebugUnsetCursor(SimpleQueueCacheCursor cursor);
    }
}
