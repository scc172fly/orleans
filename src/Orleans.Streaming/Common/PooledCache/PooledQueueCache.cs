
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// PooledQueueCache 是一种缓存，旨在用作 IQueueCache 中的消息缓存。
    /// 它能够在较长时间内（数分钟至无限期）存储大量消息（数 GB），
    /// 同时将垃圾回收带来的性能影响降至最低。
    /// 此池化缓存分配内存后不再释放，而是将已释放使用权的资源保留在池中，
    /// 这些池在服务的整个生命周期内一直供应用程序使用。这意味着这些对象会进入第 2 代（gen2），
    /// 经过压实后留在那里。其成本相对较低，因为它们现在仅产生
    /// 每次垃圾回收周期中检查是否应释放它们的成本。由于该缓存使用的对象
    /// 数量较少、体积较大且对象图相对简单，因此检查这些对象的成本
    /// 低于检查大量体积较小但对象图更复杂的对象。
    /// 出于性能考虑，此缓存的设计更贴合特定队列的数据。这也是它与 SimpleQueueCache 不同，
    /// 不直接实现 IQueueCache 的部分原因。它旨在
    /// 供针对特定队列的 IQueueCache 实现使用。
    /// </summary>
    public class PooledQueueCache : IPurgeObservable
    {
        // 消息块的链表。第一个块为最新块。
        private readonly LinkedList<CachedMessageBlock> messageBlocks;
        private readonly CachedMessagePool pool;
        private readonly ICacheDataAdapter cacheDataAdapter;
        private readonly ILogger logger;
        private readonly ICacheMonitor? cacheMonitor;
        private readonly TimeSpan purgeMetadataInterval;
        private readonly PeriodicAction? periodicMonitoring;
        private readonly PeriodicAction? periodicMetadaPurging;

        private readonly Dictionary<StreamId, (DateTime TimeStamp, StreamSequenceToken Token)> lastPurgedToken = new Dictionary<StreamId, (DateTime TimeStamp, StreamSequenceToken Token)>();

        /// <summary>
        /// 获取最近添加的缓存消息。
        /// </summary>
        public CachedMessage? Newest
        {
            get
            {
                if (IsEmpty)
                    return null;
                return messageBlocks.First!.Value.NewestMessage; // 此处 messageBlocks.Count != 0（上文已通过 IsEmpty 检查）。
            }
        }

        /// <summary>
        /// 获取缓存中最旧的消息。
        /// </summary>
        public CachedMessage? Oldest
        {
            get
            {
                if (IsEmpty)
                    return null;
                return messageBlocks.Last!.Value.OldestMessage; // 此处 messageBlocks.Count != 0（上文已通过 IsEmpty 检查）。
            }
        }

        /// <summary>
        /// 获取缓存的消息数量。
        /// </summary>
        public int ItemCount { get; private set; }

        /// <summary>
        /// 池化队列缓存是一种从池中获取资源的消息缓存。
        /// </summary>
        /// <param name="cacheDataAdapter">缓存数据适配器。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="cacheMonitorWriteInterval">缓存监视器的写入间隔。仅对活动缓存触发。</param>
        /// <param name="purgeMetadataInterval">清除缓存元数据的时间间隔。</param>
        public PooledQueueCache(
            ICacheDataAdapter cacheDataAdapter,
            ILogger logger,
            ICacheMonitor? cacheMonitor,
            TimeSpan? cacheMonitorWriteInterval,
            TimeSpan? purgeMetadataInterval = null)
        {
            this.cacheDataAdapter = cacheDataAdapter ?? throw new ArgumentNullException(nameof(cacheDataAdapter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.ItemCount = 0;
            pool = new CachedMessagePool(cacheDataAdapter);
            messageBlocks = new LinkedList<CachedMessageBlock>();
            this.cacheMonitor = cacheMonitor;
            if (this.cacheMonitor != null && cacheMonitorWriteInterval.HasValue)
            {
                this.periodicMonitoring = new PeriodicAction(cacheMonitorWriteInterval.Value, this.ReportCacheMessageStatistics);
            }

            if (purgeMetadataInterval.HasValue)
            {
                this.purgeMetadataInterval = purgeMetadataInterval.Value;
                this.periodicMetadaPurging = new PeriodicAction(purgeMetadataInterval.Value.Divide(5), this.PurgeMetadata);
            }
        }

        /// <summary>
        /// 指示缓存是否为空。
        /// </summary>
        public bool IsEmpty => messageBlocks.Count == 0 || (messageBlocks.Count == 1 && messageBlocks.First!.Value.IsEmpty); // messageBlocks.Count == 1 意味着 First 不为 null。

        /// <summary>
        /// 获取游标，从给定的 sequenceToken 位置开始枚举缓存中的消息，
        /// 并按指定的流进行筛选。
        /// </summary>
        /// <param name="streamId">流标识。</param>
        /// <param name="sequenceToken"></param>
        /// <returns>获取的缓存游标。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的序列令牌对应的位置早于缓存中最早保留的消息。
        /// </exception>
        [Obsolete("Use TryGetCursor instead.")]
        public object GetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
        {
            var result = TryGetCursor(streamId, sequenceToken);
            if (result.CacheMiss is { } cacheMiss)
            {
                throw cacheMiss.ToException();
            }

            return result.Cursor!;
        }

        /// <summary>
        /// 尝试在给定序列令牌处获取游标。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="sequenceToken">序列令牌。</param>
        /// <returns>
        /// 成功时返回包含所获取游标的结果；缓存未命中时返回包含
        /// 不可用位置和当前缓存边界的结果。
        /// </returns>
        public QueueCacheCursorResult<object> TryGetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
        {
            if (TryGetCacheMiss(streamId, sequenceToken, out var cacheMiss))
            {
                return QueueCacheCursorResult<object>.FromCacheMiss(cacheMiss);
            }

            var cursor = new Cursor(streamId);
            if (SetCursor(cursor, sequenceToken) is { } racedCacheMiss)
            {
                return QueueCacheCursorResult<object>.FromCacheMiss(racedCacheMiss);
            }

            return QueueCacheCursorResult<object>.FromCursor(cursor);
        }

        /// <summary>
        /// 尝试在指定的订阅起始位置获取游标。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>包含所获取游标的成功结果。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startPosition"/> 未定义。</exception>
        public QueueCacheCursorResult<object> TryGetCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                return TryGetCursor(streamId, null);
            }

            if (startPosition != StreamSubscriptionStartPosition.EarliestAvailable)
            {
                throw new ArgumentOutOfRangeException(nameof(startPosition), startPosition, "The subscription start position is not defined.");
            }

            var result = new Cursor(streamId);
            SetCursorAtEarliestAvailable(result);
            return QueueCacheCursorResult<object>.FromCursor(result);
        }

        /// <summary>
        /// 将空闲或未设置的游标重新定位到给定序列令牌处。
        /// </summary>
        /// <param name="cursorObj">要刷新的游标。</param>
        /// <param name="sequenceToken">用于定位游标的序列令牌。</param>
        public void Refresh(object cursorObj, StreamSequenceToken? sequenceToken)
        {
            ArgumentNullException.ThrowIfNull(cursorObj);

            if (cursorObj is not Cursor cursor)
            {
                throw new ArgumentOutOfRangeException(nameof(cursorObj), "Cursor is bad");
            }

            if (cursor.IsEarliestAvailable)
            {
                return;
            }

            // 仅重新定位空闲或未设置的游标。活动游标（Set）正在枚举，不能移动。
            // null 令牌没有重新定位目标，因此保持游标不变，让正常的空闲唤醒
            // 路径进行处理，而不是将其重新定位到最新事件（那样会跳过数据）。
            if (sequenceToken is null || cursor.State == CursorStates.Set)
            {
                return;
            }

            // 刷新始终只会将游标向前移动。如果游标已经位于请求令牌处或其之后
            // （例如，未设置的游标正在等待未来的令牌），则保持其位置不变，避免
            // 使其回溯并重新投递消费者已请求跳过的事件。
            if (cursor.SequenceToken is not null && EventSequenceTokenCompatibility.Compare(cursor.SequenceToken, sequenceToken) >= 0)
            {
                return;
            }

            cursor.State = CursorStates.Unset;
            if (SetCursor(cursor, sequenceToken) is { } cacheMiss)
            {
                throw cacheMiss.ToException();
            }
        }

        private void ReportCacheMessageStatistics()
        {
            if (this.IsEmpty)
            {
                this.cacheMonitor!.ReportMessageStatistics(null, null, null, this.ItemCount); // 仅通过 periodicMonitoring 调用，而 periodicMonitoring 仅在 cacheMonitor 不为 null 时创建。
            }
            else
            {
                var newestMessage = this.Newest!.Value; // 缓存非空（上文已检查），因此 Newest 不为 null。
                var oldestMessage = this.Oldest!.Value; // 缓存非空（上文已检查），因此 Oldest 不为 null。
                var newestMessageEnqueueTime = newestMessage.EnqueueTimeUtc;
                var oldestMessageEnqueueTime = oldestMessage.EnqueueTimeUtc;
                var oldestMessageDequeueTime = oldestMessage.DequeueTimeUtc;
                this.cacheMonitor!.ReportMessageStatistics(oldestMessageEnqueueTime, oldestMessageDequeueTime, newestMessageEnqueueTime, this.ItemCount); // 仅通过 periodicMonitoring 调用，而 periodicMonitoring 仅在 cacheMonitor 不为 null 时创建。
            }
        }

        private void PurgeMetadata()
        {
            var now = DateTime.UtcNow;

            // 获取所有早于 this.purgeMetadataInterval 所指定时限的键。
            foreach (var kvp in this.lastPurgedToken)
            {
                if (kvp.Value.TimeStamp + this.purgeMetadataInterval < now)
                {
                    lastPurgedToken.Remove(kvp.Key);
                }
            }
        }

        private void TrackAndPurgeMetadata(CachedMessage messageToRemove)
        {
            // 如果禁用了已逐出消息的元数据跟踪，则不执行任何操作。
            if (this.periodicMetadaPurging == null)
                return;

            var now = DateTime.UtcNow;
            var streamId = messageToRemove.StreamId;
            var token = this.cacheDataAdapter.GetSequenceToken(ref messageToRemove);
            this.lastPurgedToken[streamId] = (now, token);

            this.periodicMetadaPurging.TryAction(now);
        }

        private QueueCacheMissInfo? SetCursor(Cursor cursor, StreamSequenceToken? sequenceToken)
        {
            // 如果缓存为空，则取消设置令牌并等待更多数据。（译注：实现将游标置为 Unset 状态，并保留传入的序列令牌。）
            if (IsEmpty)
            {
                cursor.State = CursorStates.Unset;
                cursor.SequenceToken = sequenceToken;
                return null;
            }

            LinkedListNode<CachedMessageBlock> newestBlock = messageBlocks.First!; // messageBlocks.Count != 0（上文已检查）。

            // 如果 sequenceToken 为 null，则从缓存中的最新消息开始迭代。
            if (sequenceToken == null)
            {
                cursor.State = CursorStates.Idle;
                cursor.CurrentBlock = newestBlock;
                cursor.Index = newestBlock.Value.NewestMessageIndex;
                cursor.SequenceToken = newestBlock.Value.GetNewestSequenceToken(cacheDataAdapter);
                return null;
            }

            // 如果 sequenceToken 太新而尚未进入缓存，则取消设置令牌并等待更多数据。（译注：实现将游标置为 Unset 状态，并保留该序列令牌。）
            CachedMessage newestMessage = newestBlock.Value.NewestMessage;
            if (cacheDataAdapter.Compare(ref newestMessage, sequenceToken) < 0)
            {
                cursor.State = CursorStates.Unset;
                cursor.SequenceToken = sequenceToken;
                return null;
            }

            // 检查 sequenceToken 是否太旧而不在缓存中。
            var oldestBlock = messageBlocks.Last!; // messageBlocks.Count != 0（上文已检查）。
            var oldestMessage = oldestBlock.Value.OldestMessage;
            if (cacheDataAdapter.Compare(ref oldestMessage, sequenceToken) > 0)
            {
                // 检查上次清除缓存后是否遗漏了事件。
                if (this.lastPurgedToken.TryGetValue(cursor.StreamId, out var entry) && EventSequenceTokenCompatibility.Compare(sequenceToken, entry.Token) >= 0)
                {
                    // 如果令牌比最近清除的令牌更新，则未遗漏任何消息。从缓存中最旧的消息开始。
                    cursor.State = CursorStates.Set;
                    cursor.CurrentBlock = oldestBlock;
                    cursor.Index = oldestBlock.Value.OldestMessageIndex;
                    cursor.SequenceToken = oldestBlock.Value.GetOldestSequenceToken(cacheDataAdapter);
                    return null;
                }
                else
                {
                    return CreateCacheMissInfo(sequenceToken);
                }
            }

            // 从最新块向最旧块查找包含该序列号的块。
            LinkedListNode<CachedMessageBlock>? node = messageBlocks.First;
            while (true)
            {
                CachedMessage oldestMessageInBlock = node!.Value.OldestMessage; // 循环不变式：只要搜索尚未遍历完缓存，node 就不为 null（由上文的边界检查保证）。
                if (cacheDataAdapter.Compare(ref oldestMessageInBlock, sequenceToken) <= 0)
                {
                    break;
                }
                node = node.Next;
            }

            // 从起始位置返回游标。
            cursor.CurrentBlock = node;
            cursor.Index = node!.Value.GetIndexOfFirstMessageLessThanOrEqualTo(sequenceToken, cacheDataAdapter); // 参见上文的循环不变式。
            // 如果游标此前处于空闲状态，则移至 sequenceToken 指定消息的下一条消息。
            if (cursor.State == CursorStates.Idle)
            {
                // 如果当前块中还有消息，则移至下一条消息。
                if (!cursor.IsNewestInBlock)
                {
                    cursor.Index++;
                }
                // 如果这是当前块中的最新消息，则移至较新块中最旧的消息。
                else if (node.Previous != null)
                {
                    cursor.CurrentBlock = node.Previous;
                    cursor.Index = cursor.CurrentBlock!.Value.OldestMessageIndex; // 刚刚赋值为 node.Previous，此处该值不为 null。
                }
                else
                {
                    cursor.State = CursorStates.Idle;
                    return null;
                }
            }
            cursor.SequenceToken = cursor.CurrentBlock!.Value.GetSequenceToken(cursor.Index, cacheDataAdapter); // 到达此处的每条执行路径都已在上文将 CurrentBlock 设置为有效块。
            cursor.State = CursorStates.Set;
            return null;
        }

        private bool TryGetCacheMiss(
            StreamId streamId,
            StreamSequenceToken? sequenceToken,
            out QueueCacheMissInfo cacheMiss)
        {
            if (sequenceToken is null || IsEmpty)
            {
                cacheMiss = default;
                return false;
            }

            var newestBlock = messageBlocks.First!;
            var newestMessage = newestBlock.Value.NewestMessage;
            if (newestMessage.Compare(sequenceToken) < 0)
            {
                cacheMiss = default;
                return false;
            }

            var oldestMessage = messageBlocks.Last!.Value.OldestMessage;
            if (oldestMessage.Compare(sequenceToken) <= 0
                || lastPurgedToken.TryGetValue(streamId, out var entry)
                    && EventSequenceTokenCompatibility.Compare(sequenceToken, entry.Token) >= 0)
            {
                cacheMiss = default;
                return false;
            }

            cacheMiss = CreateCacheMissInfo(sequenceToken);
            return true;
        }

        private void SetCursorAtEarliestAvailable(Cursor cursor)
        {
            for (var node = messageBlocks.Last; node is not null; node = node.Previous)
            {
                if (node.Value.TryFindFirstMessage(cursor.StreamId, cacheDataAdapter, out var index))
                {
                    cursor.State = CursorStates.EarliestAvailableSet;
                    cursor.CurrentBlock = node;
                    cursor.Index = index;
                    cursor.SequenceToken = node.Value.GetSequenceToken(index, cacheDataAdapter);
                    cursor.BlockGeneration = node.Value.Generation;
                    return;
                }
            }

            SetWaitingAtCurrentEnd(cursor);
        }

        private void SetWaitingAtCurrentEnd(Cursor cursor)
        {
            cursor.State = CursorStates.EarliestAvailableWaiting;
            cursor.SequenceToken = null;
            cursor.CurrentBlock = messageBlocks.First;
            cursor.BlockGeneration = cursor.CurrentBlock?.Value.Generation ?? 0;
            cursor.Index = cursor.CurrentBlock?.Value.WriteIndex ?? 0;
        }

        private static void SetWaitingAfter(Cursor cursor, LinkedListNode<CachedMessageBlock> block, int index)
        {
            cursor.State = CursorStates.EarliestAvailableWaiting;
            cursor.SequenceToken = null;
            cursor.CurrentBlock = block;
            cursor.BlockGeneration = block.Value.Generation;
            cursor.Index = index + 1;
        }

        private bool TrySetCursorAtFirstMatchingMessageAfterAnchor(Cursor cursor)
        {
            LinkedListNode<CachedMessageBlock>? node;
            int startIndex;
            if (cursor.CurrentBlock is { } anchor
                && anchor.List == messageBlocks
                && anchor.Value.Generation == cursor.BlockGeneration)
            {
                node = anchor;
                startIndex = Math.Max(cursor.Index, anchor.Value.OldestMessageIndex);
            }
            else
            {
                node = messageBlocks.Last;
                startIndex = node?.Value.OldestMessageIndex ?? 0;
            }

            while (node is not null)
            {
                if (!node.Value.IsEmpty
                    && startIndex < node.Value.WriteIndex
                    && node.Value.TryFindNextMessage(startIndex, cursor.StreamId, cacheDataAdapter, out var index))
                {
                    cursor.State = CursorStates.EarliestAvailableSet;
                    cursor.CurrentBlock = node;
                    cursor.Index = index;
                    cursor.SequenceToken = node.Value.GetSequenceToken(index, cacheDataAdapter);
                    cursor.BlockGeneration = node.Value.Generation;
                    return true;
                }

                node = node.Previous;
                startIndex = node?.Value.OldestMessageIndex ?? 0;
            }

            SetWaitingAtCurrentEnd(cursor);
            return false;
        }

        /// <summary>
        /// 从缓存中获取给定游标处的下一条消息。
        /// </summary>
        /// <param name="cursorObj"></param>
        /// <param name="message"></param>
        /// <returns><see langword="true"/> 表示已返回消息；否则为 <see langword="false"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cursorObj"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cursorObj"/> 不是缓存游标。</exception>
        /// <exception cref="QueueCacheMissException">
        /// 游标位置早于缓存所保留的消息。
        /// </exception>
        [Obsolete("Use TryGetNextMessageWithResult instead.")]
        public bool TryGetNextMessage(object cursorObj, [NotNullWhen(true)] out IBatchContainer? message)
        {
            var result = TryGetNextMessageWithResult(cursorObj, out message);
            if (result.CacheMiss is { } cacheMiss)
            {
                throw cacheMiss.ToException();
            }

            return result.Kind == QueueCacheCursorMoveResultKind.Success;
        }

        /// <summary>
        /// 尝试从缓存中获取给定游标处的下一条消息。
        /// </summary>
        /// <param name="cursorObj">缓存游标。</param>
        /// <param name="message">下一条可用消息。</param>
        /// <returns>
        /// 返回包含非 null <paramref name="message"/> 的成功结果、<see cref="QueueCacheCursorMoveResultKind.NoData"/> 结果（
        /// 其消息为 null），或包含 null 消息的缓存未命中结果。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="cursorObj"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cursorObj"/> 不是缓存游标。</exception>
        public QueueCacheCursorMoveResult TryGetNextMessageWithResult(object cursorObj, out IBatchContainer? message)
        {
            message = null;

            if (cursorObj == null)
            {
                throw new ArgumentNullException(nameof(cursorObj));
            }

            var cursor = cursorObj as Cursor;
            if (cursor == null)
            {
                throw new ArgumentOutOfRangeException(nameof(cursorObj), "Cursor is bad");
            }

            if (cursor.State == CursorStates.EarliestAvailableSet
                && (cursor.CurrentBlock?.List != messageBlocks
                    || !cursor.CurrentBlock.Value.Contains(cursor.Index, cursor.BlockGeneration)))
            {
                if (cursor.CurrentBlock is { } currentBlock
                    && currentBlock.List == messageBlocks
                    && currentBlock.Value.Generation == cursor.BlockGeneration)
                {
                    SetWaitingAfter(cursor, currentBlock, cursor.Index);
                }
                else
                {
                    cursor.CurrentBlock = null;
                    cursor.BlockGeneration = 0;
                    cursor.Index = 0;
                    cursor.State = CursorStates.EarliestAvailableWaiting;
                }
            }

            if (cursor.State == CursorStates.EarliestAvailableWaiting
                && !TrySetCursorAtFirstMatchingMessageAfterAnchor(cursor))
            {
                return QueueCacheCursorMoveResult.NoData;
            }

            if (cursor.State is not CursorStates.Set and not CursorStates.EarliestAvailableSet)
            {
                if (SetCursor(cursor, cursor.SequenceToken) is { } cacheMiss)
                {
                    return QueueCacheCursorMoveResult.FromCacheMiss(cacheMiss);
                }

                if (cursor.State != CursorStates.Set)
                {
                    return QueueCacheCursorMoveResult.NoData;
                }
            }

            // 此消息是否已被清除？
            CachedMessage oldestMessage = messageBlocks.Last!.Value.OldestMessage; // 游标处于 Set 状态，因此缓存非空。
            if (cursor.State == CursorStates.Set
                && cacheDataAdapter.Compare(ref oldestMessage, cursor.SequenceToken!) > 0) // 游标处于 Set 状态，因此 SequenceToken 保证不为 null。
            {
                return QueueCacheCursorMoveResult.FromCacheMiss(CreateCacheMissInfo(cursor.SequenceToken!));
            }

            // 在缓存中按时间顺序向前迭代，直到找到属于该流的消息或遍历完缓存中的消息。
            // 注意，先获取当前游标位置的消息，再将游标向前移动。这意味着如果返回 true，则游标
            // 将指向所返回消息的下一条消息。
            while (cursor.State is CursorStates.Set or CursorStates.EarliestAvailableSet)
            {
                CachedMessage currentMessage = cursor.Message;

                // 是否已追上最新事件？若是，则将游标设为空闲状态。
                if (cursor.CurrentBlock == messageBlocks.First && cursor.IsNewestInBlock)
                {
                    if (cursor.State == CursorStates.EarliestAvailableSet)
                    {
                        SetWaitingAfter(cursor, cursor.CurrentBlock!, cursor.Index);
                    }
                    else
                    {
                        cursor.State = CursorStates.Idle;
                        cursor.SequenceToken = messageBlocks.First!.Value.GetNewestSequenceToken(cacheDataAdapter); // 刚刚比较得出其等于 cursor.CurrentBlock，而在 cursor.State 为 Set 时该值不为 null。
                    }
                }
                else // 移至下一条。
                {
                    int index;
                    if (cursor.IsNewestInBlock)
                    {
                        // 当 cursor.State 为 Set 时，cursor.CurrentBlock 不为 null。此处它不是 messageBlocks.First（上文已检查），
                        // 因此它不是缓存中的最新块，其 Previous 不为 null。
                        cursor.CurrentBlock = cursor.CurrentBlock!.Previous;
                        cursor.CurrentBlock!.Value.TryFindFirstMessage(cursor.StreamId, this.cacheDataAdapter, out index);
                    }
                    else
                    {
                        cursor.CurrentBlock!.Value.TryFindNextMessage(cursor.Index + 1, cursor.StreamId, this.cacheDataAdapter, out index); // 当 cursor.State 为 Set 时不为 null。
                    }
                    cursor.Index = index;
                    if (cursor.State == CursorStates.EarliestAvailableSet)
                    {
                        cursor.BlockGeneration = cursor.CurrentBlock!.Value.Generation;
                    }
                }

                // 检查此消息是否属于游标对应的流。
                if (currentMessage.CompareStreamId(cursor.StreamId))
                {
                    message = cacheDataAdapter.GetBatchContainer(ref currentMessage);
                    cursor.SequenceToken = cursor.IsEarliestAvailable
                        ? cacheDataAdapter.GetSequenceToken(ref currentMessage)
                        : cursor.CurrentBlock!.Value.GetSequenceToken(cursor.Index, cacheDataAdapter); // 当 cursor.State 为 Set 时不为 null。
                    return QueueCacheCursorMoveResult.Success;
                }
            }

            return QueueCacheCursorMoveResult.NoData;
        }

        private QueueCacheMissInfo CreateCacheMissInfo(StreamSequenceToken requestedToken)
            => new(
                requestedToken,
                messageBlocks.Last!.Value.GetOldestSequenceToken(cacheDataAdapter),
                messageBlocks.First!.Value.GetNewestSequenceToken(cacheDataAdapter));

        /// <summary>
        /// 将一组队列消息添加到缓存中。
        /// </summary>
        /// <param name="messages"></param>
        /// <param name="dequeueTime"></param>
        /// <returns></returns>
        public void Add(List<CachedMessage> messages, DateTime dequeueTime)
        {
            foreach (var message in messages)
            {
                this.Add(message);
            }
            this.cacheMonitor?.TrackMessagesAdded(messages.Count);
            periodicMonitoring?.TryAction(dequeueTime);
        }

        private void Add(CachedMessage message)
        {
            // 从池中分配消息。
            CachedMessageBlock block = pool.AllocateMessage(message);

            // 如果是新块，则将消息块添加到链表中。
            if (block != messageBlocks.FirstOrDefault())
                messageBlocks.AddFirst(block.Node);
            ItemCount++;
        }

        /// <summary>
        /// 移除缓存中最旧的消息，如果最旧块已空，则也移除该块。
        /// </summary>
        public void RemoveOldestMessage()
        {
            TrackAndPurgeMetadata(this.messageBlocks.Last!.Value.OldestMessage); // 仅在缓存非空时调用。

            this.messageBlocks.Last!.Value.Remove(); // 仅在缓存非空时调用。
            this.ItemCount--;
            CachedMessageBlock lastCachedMessageBlock = this.messageBlocks.Last!.Value; // 仅在缓存非空时调用。
            // 如果当前块已空且容量已经全部耗尽，则移除该块。
            if (lastCachedMessageBlock.IsEmpty && !lastCachedMessageBlock.HasCapacity)
            {
                lastCachedMessageBlock.Dispose();
                this.messageBlocks.RemoveLast();
            }
        }

        private enum CursorStates
        {
            Unset, // 尚未设置，或指向未来的某些数据。
            Set, // 指向缓存中的一条消息。
            Idle, // 已遍历缓存中所有相关事件，正在等待流上的更多数据。
            EarliestAvailableSet, // 指向为“最早可用”订阅按插入顺序选定的消息。
            EarliestAvailableWaiting, // 在缓存的物理位置之后等待下一条匹配的流消息。
        }

        private class Cursor
        {
            public readonly StreamId StreamId;

            public Cursor(StreamId streamId)
            {
                StreamId = streamId;
                State = CursorStates.Unset;
            }

            public CursorStates State;

            // 当前序列令牌；在 Unset 状态时为 null（尚未建立序列令牌）。（译注：等待未来位置时，Unset 状态也可保留非 null 令牌。）
            public StreamSequenceToken? SequenceToken;
            public long BlockGeneration;

            // 对缓存内部的引用；当 State 为 Set 时不为 null。
            public LinkedListNode<CachedMessageBlock>? CurrentBlock;
            public int Index;

            // 辅助功能。
            public bool IsEarliestAvailable => State is CursorStates.EarliestAvailableSet or CursorStates.EarliestAvailableWaiting;
            public bool IsNewestInBlock => Index == CurrentBlock!.Value.NewestMessageIndex; // 仅在 State 为 Set 时访问，此时 CurrentBlock 不为 null。
            public CachedMessage Message => CurrentBlock!.Value[Index]; // 仅在 State 为 Set 时访问，此时 CurrentBlock 不为 null。
        }
    }
}
