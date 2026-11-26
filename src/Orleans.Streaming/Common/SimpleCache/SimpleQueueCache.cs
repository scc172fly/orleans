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
        // 用于后压检测 我们保持10桶的直方图
        // 每一份桶子记录 有多少物品在那个桶子里的储藏处
        // 有多少光标在水桶里 投向一个东西
        // 当我们添加和删除缓存物品(可能打开或去除桶)时,我们将更新 NumCurrentItems
        // 每次移动光标时,我们都会更新 NumCurrentCursors
        // 如果第一个(最过时的桶)至少有一个光标指向它,我们说我们受到背压(完全缓存)。
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
        internal IBatchContainer Batch = null!; // 构造后总是立即通过对象初始化器设置 。
        internal bool DeliveryFailure;
        internal StreamSequenceToken SequenceToken = null!; // 构造后总是立即通过对象初始化器设置 。
        internal CacheBucket CacheBucket = null!; // 构造后总是立即通过对象初始化器设置 。
    }

    /// <summary>
    /// 保存项目的存储的队列缓存 。
    /// </summary>
    public partial class SimpleQueueCache : IQueueCache
    {
        private readonly LinkedList<SimpleQueueCacheItem> cachedMessages;
        private readonly int maxCacheSize;
        private readonly ILogger logger;
        private readonly List<CacheBucket> cacheCursorHistogram; // 用于后压力检测
        private readonly Func<SimpleQueueCache, StreamId, SimpleQueueCacheCursor>? cursorFactory;
        private const int NUM_CACHE_HISTOGRAM_BUCKETS = 10;
        private readonly int CACHE_HISTOGRAM_MAX_BUCKET_SIZE;

        /// <summary>
        /// 获取缓存中的项目数 。
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
        /// <param name="cacheSize">缓存的大小。</param>
        /// <param name="logger">日志器。</param>
        public SimpleQueueCache(int cacheSize, ILogger logger)
            : this(cacheSize, logger, cursorFactory: null)
        {
        }

        // 测试可以观察光标的所有权,而不暴露公共工厂钩。
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
            CACHE_HISTOGRAM_MAX_BUCKET_SIZE = Math.Max(cacheSize / NUM_CACHE_HISTOGRAM_BUCKETS, 1); // 10桶,我们有10桶
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
            if (cachedMessages.Count == 0) return false; // 空空缓存
            if (cacheCursorHistogram.Count == 0) return false;  // 光标尚未显示 - 基本为零消费者。
            if (cacheCursorHistogram[0].NumCurrentCursors > 0) return false; // 消费者仍然活跃在最古老的桶子-快路中

            var allItems = new List<IBatchContainer>();
            while (cacheCursorHistogram.Count > 0 && cacheCursorHistogram[0].NumCurrentCursors == 0)
            {
                List<IBatchContainer> items = DrainBucket(cacheCursorHistogram[0]);
                allItems.AddRange(items);
                cacheCursorHistogram.RemoveAt(0); // 删除最后一个桶
            }
            purgedItems = allItems;

            LogDebugTryPurgeFromCache(allItems.Count);

            return true;
        }

        private List<IBatchContainer> DrainBucket(CacheBucket bucket)
        {
            var itemsToRelease = new List<IBatchContainer>(bucket.NumCurrentItems);
            // 缓存中从上开始的所有行进项目
            // 并搬出储藏处 把那些藏在给定桶里的东西搬走 直到我们跳到下一个桶
            while (bucket.NumCurrentItems > 0)
            {
                SimpleQueueCacheItem item = cachedMessages.Last!.Value; // bucket.NumCurrentItems > 0 表示清单是非空白的。
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
                    // 此项目已经指向下一桶, 所以停止 。
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
        /// 请求的符名比缓存保存的信件要旧 。
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
#pragma warning disable CS0618 // Required for compatibility with derived caches which override the legacy method.
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

            // 缓存、未设置的标牌和等待更多数据等信息中没有任何内容。
            if (cachedMessages.Count == 0)
            {
                UnsetCursor(cursor, sequenceToken);
                return null;
            }

            // 如果未提供标记, 则在缓存结束处设置项目上的标记
            sequenceToken = sequenceToken ?? cachedMessages.First?.Value?.SequenceToken!; // cachedMessages.Count > 0 (上文已作核对),因此Ist/Value/SequenceToken 得到保证不中。

            // 如果 sequenceToken 过于新, 无法在缓存、 取消设置标记并等待更多数据 。
            if (EventSequenceTokenCompatibility.Compare(sequenceToken, cachedMessages.First!.Value.SequenceToken) > 0) // cachedMessages.Count > 0 (上文已作核对), 所以首先保证不停。
            {
                UnsetCursor(cursor, sequenceToken);
                return null;
            }

            LinkedListNode<SimpleQueueCacheItem> lastMessage = cachedMessages.Last!; // cachedMessages.Count > 0 (上文已检查) 这里, 所以 Last 是有保证的不中
            // 检查以查看偏移是否太旧,无法在缓存中
            if (EventSequenceTokenCompatibility.Compare(sequenceToken, lastMessage.Value.SequenceToken) < 0)
            {
                return CreateCacheMissInfo(sequenceToken);
            }

            // 现在,请求的 sequenceToken 已经设置,并且也处于缓存限度之内。

            // 查找首封信件, 时间或下方
            // 事件从最新的到最老的顺序排列, 因此从列表开始循环,直到我们之前的偏移点或结尾点发生。
            LinkedListNode<SimpleQueueCacheItem>? node = cachedMessages.First;
            while (node != null && EventSequenceTokenCompatibility.Compare(node.Value.SequenceToken, sequenceToken) > 0)
            {
                // 我们到最后了吗?
                if (node.Next == null) // 节点是最后一条消息
                    break;

                // 如果 sequenceId 介于两之间,取高者
                if (EventSequenceTokenCompatibility.Compare(node.Next.Value.SequenceToken, sequenceToken) < 0)
                    break;

                node = node.Next;
            }

            // 从一开始就返回光标。
            SetCursor(cursor, node!); // 保证无核:节点开始为第一(自数 > 0 起为非中值),并且只有在进行非中值检查后才被重新分配给 node.Next 。
            return null;
        }

        internal void RefreshCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? sequenceToken)
        {
            LogDebugRefreshCursor(cursor, sequenceToken);

            // 设置
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
        /// 获取在所提供光标的缓存中下一个消息
        /// </summary>
        /// <param name="cursor"></param>
        /// <param name="batch"></param>
        /// <returns></returns>
        internal bool TryGetNextMessage(SimpleQueueCacheCursor cursor, out IBatchContainer? batch)
        {
            LogDebugTryGetNextMessage(cursor);

            batch = null;
            if (!cursor.IsSet) return false;

            // 如果我们在缓存未设置光标的结尾处, 并向前移动抵消一个
            if (cursor.Element == cachedMessages.First)
            {
                UnsetCursor(cursor, null);
            }
            else // 向下贴贴:
            {
                AdvanceCursor(cursor, cursor.Element!.Previous!); // cursor.IsSet 元素是真实的,所以元素是非null;元素!== 1⁄4它有前一个节点。
            }

            batch = cursor.Element?.Value.Batch;
            return (batch != null);
        }

        private void AdvanceCursor(SimpleQueueCacheCursor cursor, LinkedListNode<SimpleQueueCacheItem> item)
        {
            LogDebugUpdateCursor(cursor, item.Value.Batch);

            cursor.Element!.Value.CacheBucket.UpdateNumCursors(-1); // 从前桶中移除; cursor.Element 这里不中号( 见调用者)。
            cursor.Set(item);
            cursor.Element!.Value.CacheBucket.UpdateNumCursors(1);  // 添加到下一桶
        }

        internal void SetCursor(SimpleQueueCacheCursor cursor, LinkedListNode<SimpleQueueCacheItem> item)
        {
            LogDebugSetCursor(cursor, item.Value.Batch);

            cursor.Set(item);
            cursor.Element!.Value.CacheBucket.UpdateNumCursors(1);  // 添加到下一个桶; cursor.Set(项目)以上保证,元素为无核。
        }

        internal void UnsetCursor(SimpleQueueCacheCursor cursor, StreamSequenceToken? token)
        {
            LogDebugUnsetCursor(cursor);

            if (cursor.IsSet)
            {
                cursor.Element!.Value.CacheBucket.UpdateNumCursors(-1); // cursor.IsSet 是真的,所以元素是非核。
            }
            cursor.UnSet(token);
        }

        private void Add(IBatchContainer batch, StreamSequenceToken sequenceToken)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            // 绝不应当发生这种情况,但以防
            if (Size >= maxCacheSize) throw new CacheFullException();

            CacheBucket cacheBucket;
            if (cacheCursorHistogram.Count == 0)
            {
                cacheBucket = new CacheBucket();
                cacheCursorHistogram.Add(cacheBucket);
            }
            else
            {
                cacheBucket = cacheCursorHistogram[cacheCursorHistogram.Count - 1]; // 最后最后一个
            }

            if (cacheBucket.NumCurrentItems == CACHE_HISTOGRAM_MAX_BUCKET_SIZE) // 最后一个桶满了,打开新的
            {
                cacheBucket = new CacheBucket();
                cacheCursorHistogram.Add(cacheBucket);
            }

            // 添加信件到链接列表
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
