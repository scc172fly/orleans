
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
    /// PooledQueueCache 是一个缓存,意在作为 IQueueCache中的信息缓存。
    /// 它能够长期储存大量信息(价值的图像)
    ///   时间(分钟至无限期),但垃圾收集工作导致最低性能打击。
    /// 这个集合缓存共享共享存储器分配记忆,永远不释放。 它保持了共用资源中可自由使用的资源 
    ///   使用寿命期间仍在应用中的物体,即这些物体进入第2款。
    ///   相对便宜,因为现在他们唯一的成本是
    ///   检查成本,以确定是否在每个收集周期中释放这些弹药。
    ///   少量大物体,其对象图相对简单,检查费用较低
    ///   然后是大量较小对象,其物体图形更为复杂。
    /// 基于性能原因,该缓存的设计与队列特定数据更加一致。 也就是说,
    ///   部分原因,为什么与 SimpleQueueCache不同,这个缓存不执行 IQueueCache。
    ///   用于 IQueueCache的队列特定执行。
    /// </summary>
    public class PooledQueueCache : IPurgeObservable
    {
        // 信件 bocks 链接列表。 第一个是最新消息 。
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
        /// 获取最近添加的缓存信件。
        /// </summary>
        public CachedMessage? Newest
        {
            get
            {
                if (IsEmpty)
                    return null;
                return messageBlocks.First!.Value.NewestMessage; // messageBlocks.Count ! = 0 (通过 IsEmpty核对以上) 。
            }
        }

        /// <summary>
        /// 获取缓存中最古老的信息 。
        /// </summary>
        public CachedMessage? Oldest
        {
            get
            {
                if (IsEmpty)
                    return null;
                return messageBlocks.Last!.Value.OldestMessage; // messageBlocks.Count ! = 0 (通过 IsEmpty核对以上) 。
            }
        }

        /// <summary>
        /// 获取缓存信件计数 。
        /// </summary>
        public int ItemCount { get; private set; }

        /// <summary>
        /// 集合队列缓存是一个从池中获取资源的信息缓存
        /// </summary>
        /// <param name="cacheDataAdapter">缓存数据适配器。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="cacheMonitorWriteInterval">缓存监视器写间隔。 只有活动缓存才触发 。</param>
        /// <param name="purgeMetadataInterval">清除缓存元数据的间隔。</param>
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
        /// 指示缓存是否空
        /// </summary>
        public bool IsEmpty => messageBlocks.Count == 0 || (messageBlocks.Count == 1 && messageBlocks.First!.Value.IsEmpty); // messageBlocks.Count = 1 表示第一是非核。

        /// <summary>
        /// 获取光标,通过所提供 sequenceToken的缓存中的信件进行罗列, 
        ///   过滤到指定的流上。
        /// </summary>
        /// <param name="streamId">流态身份</param>
        /// <param name="sequenceToken"></param>
        /// <returns>获得的缓存光标。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的符名比缓存保存的信件要旧 。
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
        /// 试图在提供的序列符号上获取光标 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="sequenceToken">序列符号。</param>
        /// <returns>
        /// 包含获得的光标的成功结果,或含有
        /// 无法获取位置和当前缓存边框 。
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
        /// 试图在指定的订阅启动位置获取光标 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>包含所获取光标的成功结果。</returns>
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
        /// 在提供的序列符号上重新定位闲置或未设定光标。
        /// </summary>
        /// <param name="cursorObj">光标要刷新。</param>
        /// <param name="sequenceToken">将光标定位在位置上的序列符号。</param>
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

            // 只调整空闲或未设置光标的位置。 活动光标( Set) 是中数, 不得移动 。
            // 无一无名牌没有重新定位目标, 所以不要让光标保持未动, 让普通的空闲唤醒
            // 路径处理它, 而不是重新定位到最新事件( 这会跳过数据) 。
            if (sequenceToken is null || cursor.State == CursorStates.Set)
            {
                return;
            }

            // 刷新时只往前移动光标。如果光标已定位在鼠标的前面或正前
            // 请求的牌号( 例如, 尚未设置的光标等待未来的牌) , 请将其留在原位, 所以我们可以这样做
            // 消费者已要求从此开始,
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
                this.cacheMonitor!.ReportMessageStatistics(null, null, null, this.ItemCount); // 仅通过 periodicMonitoring引用, 只有在 cacheMonitor 不中值时才创建。
            }
            else
            {
                var newestMessage = this.Newest!.Value; // 并非空( 上文选中) , 所以新时是非中点 。
                var oldestMessage = this.Oldest!.Value; // 并非空( 已在上面选中) , 所以老者是非null 。
                var newestMessageEnqueueTime = newestMessage.EnqueueTimeUtc;
                var oldestMessageEnqueueTime = oldestMessage.EnqueueTimeUtc;
                var oldestMessageDequeueTime = oldestMessage.DequeueTimeUtc;
                this.cacheMonitor!.ReportMessageStatistics(oldestMessageEnqueueTime, oldestMessageDequeueTime, newestMessageEnqueueTime, this.ItemCount); // 仅通过 periodicMonitoring引用, 只有在 cacheMonitor 不中值时才创建。
            }
        }

        private void PurgeMetadata()
        {
            var now = DateTime.UtcNow;

            // 获取所有重于 this.purgeMetadataInterval的密钥
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
            // 如果已禁用对被逐出信件元数据的跟踪, 则不做任何动作
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
            // 如果在缓存、取消标记和等待更多数据中没有任何信息, 则取消设置标记。
            if (IsEmpty)
            {
                cursor.State = CursorStates.Unset;
                cursor.SequenceToken = sequenceToken;
                return null;
            }

            LinkedListNode<CachedMessageBlock> newestBlock = messageBlocks.First!; // messageBlocks.Count =0(上文已作核对)。

            // 如果 sequenceToken 无效,则从缓存中最新信件转出
            if (sequenceToken == null)
            {
                cursor.State = CursorStates.Idle;
                cursor.CurrentBlock = newestBlock;
                cursor.Index = newestBlock.Value.NewestMessageIndex;
                cursor.SequenceToken = newestBlock.Value.GetNewestSequenceToken(cacheDataAdapter);
                return null;
            }

            // 如果 sequenceToken 过于新, 无法在缓存、 取消设置标记并等待更多数据 。
            CachedMessage newestMessage = newestBlock.Value.NewestMessage;
            if (cacheDataAdapter.Compare(ref newestMessage, sequenceToken) < 0)
            {
                cursor.State = CursorStates.Unset;
                cursor.SequenceToken = sequenceToken;
                return null;
            }

            // 检查是否已经老到无法在缓存中 sequenceToken
            var oldestBlock = messageBlocks.Last!; // messageBlocks.Count =0(上文已作核对)。
            var oldestMessage = oldestBlock.Value.OldestMessage;
            if (cacheDataAdapter.Compare(ref oldestMessage, sequenceToken) > 0)
            {
                // 检查我们上次清理缓存后是否错过了事件
                if (this.lastPurgedToken.TryGetValue(cursor.StreamId, out var entry) && EventSequenceTokenCompatibility.Compare(sequenceToken, entry.Token) >= 0)
                {
                    // 如果该牌的最近时间比上次清除的牌更近, 那么我们没有丢失任何东西。 从缓存中最老的信件开始
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

            // 查找含有序列号的块块, 从最新开始, 并返回到最老
            LinkedListNode<CachedMessageBlock>? node = messageBlocks.First;
            while (true)
            {
                CachedMessage oldestMessageInBlock = node!.Value.OldestMessage; // 循环变量:当搜索没有用尽缓存(受上述界限检查的保证)时,节点不是中核。
                if (cacheDataAdapter.Compare(ref oldestMessageInBlock, sequenceToken) <= 0)
                {
                    break;
                }
                node = node.Next;
            }

            // 从一开始就返回光标。
            cursor.CurrentBlock = node;
            cursor.Index = node!.Value.GetIndexOfFirstMessageLessThanOrEqualTo(sequenceToken, cacheDataAdapter); // 见上文“循环”一行。
            // 如果光标空闲, 则在 sequenceToken指定信件后移动到下一条信件  
            if (cursor.State == CursorStates.Idle)
            {
                // 如果此块块中有更多的信件, 移动到下一条信件
                if (!cursor.IsNewestInBlock)
                {
                    cursor.Index++;
                }
                // 如果这是此块中最新的信件, 请移动到新块中最老的信件
                else if (node.Previous != null)
                {
                    cursor.CurrentBlock = node.Previous;
                    cursor.Index = cursor.CurrentBlock!.Value.OldestMessageIndex; // 刚刚指定给 node.Previous, 这里不在此内。
                }
                else
                {
                    cursor.State = CursorStates.Idle;
                    return null;
                }
            }
            cursor.SequenceToken = cursor.CurrentBlock!.Value.GetSequenceToken(cursor.Index, cacheDataAdapter); // CurrentBlock 被设置为达到此点的每一路径以上有效区块。
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
        /// 获取在所提供光标的缓存中下一个消息
        /// </summary>
        /// <param name="cursorObj"></param>
        /// <param name="message"></param>
        /// <returns><see langword="true"/> 电文被退回时;否则, <see langword="false"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cursorObj"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cursorObj"/> 不是一个缓存光标。</exception>
        /// <exception cref="QueueCacheMissException">
        /// 光标位置比缓存保存的信件要老。
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
        /// 试图获取在提供的光标处缓存中的下一个消息 。
        /// </summary>
        /// <param name="cursorObj">缓存光标。</param>
        /// <param name="message">下一条消息可用时的下一个消息。</param>
        /// <returns>
        /// 无中点 <paramref name="message"/>、 <see cref="QueueCacheCursorMoveResultKind.NoData"/>的成功结果
        /// 带有无效信息,或含有无效信息的缓存遗漏结果。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="cursorObj"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cursorObj"/> 不是一个缓存光标。</exception>
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

            // 此消息是否已被清除
            CachedMessage oldestMessage = messageBlocks.Last!.Value.OldestMessage; // 光标是设置, 所以缓存不是空的 。
            if (cursor.State == CursorStates.Set
                && cacheDataAdapter.Compare(ref oldestMessage, cursor.SequenceToken!) > 0) // 光标是设置的, 所以 SequenceToken 保证不中核 。
            {
                return QueueCacheCursorMoveResult.FromCacheMiss(CreateCacheMissInfo(cursor.SequenceToken!));
            }

            // 在缓存中( 及时) 向前插入, 直到我们找到流上的信息 或者隐藏信件的耗尽 。
            // 请注意, 我们从当前光标所在的位置获取消息, 然后将其向前移动。 这意味着如果我们返回真实信息, 光标将返回真实信息
            //   将指向下一个信息后, 我们返回的。
            while (cursor.State is CursorStates.Set or CursorStates.EarliestAvailableSet)
            {
                CachedMessage currentMessage = cursor.Message;

                // 我们是否赶上了最新的事件, 如果这样的话, 将光标设为空闲 。
                if (cursor.CurrentBlock == messageBlocks.First && cursor.IsNewestInBlock)
                {
                    if (cursor.State == CursorStates.EarliestAvailableSet)
                    {
                        SetWaitingAfter(cursor, cursor.CurrentBlock!, cursor.Index);
                    }
                    else
                    {
                        cursor.State = CursorStates.Idle;
                        cursor.SequenceToken = messageBlocks.First!.Value.GetNewestSequenceToken(cacheDataAdapter); // 与 cursor.CurrentBlock等值, 即不中和而 cursor.State 是设置的。
                    }
                }
                else // 移到下移动
                {
                    int index;
                    if (cursor.IsNewestInBlock)
                    {
                        // cursor.CurrentBlock 是在设置 cursor.State 时不中点的。 这里不是 messageBlocks.First (上文已选中)
                        // 所以它不是缓存中最新的块, 因此有一个非null 上一个 。
                        cursor.CurrentBlock = cursor.CurrentBlock!.Previous;
                        cursor.CurrentBlock!.Value.TryFindFirstMessage(cursor.StreamId, this.cacheDataAdapter, out index);
                    }
                    else
                    {
                        cursor.CurrentBlock!.Value.TryFindNextMessage(cursor.Index + 1, cursor.StreamId, this.cacheDataAdapter, out index); // 设定 cursor.State 时不中点 。
                    }
                    cursor.Index = index;
                    if (cursor.State == CursorStates.EarliestAvailableSet)
                    {
                        cursor.BlockGeneration = cursor.CurrentBlock!.Value.Generation;
                    }
                }

                // 选中此项中是否在光标的流中
                if (currentMessage.CompareStreamId(cursor.StreamId))
                {
                    message = cacheDataAdapter.GetBatchContainer(ref currentMessage);
                    cursor.SequenceToken = cursor.IsEarliestAvailable
                        ? cacheDataAdapter.GetSequenceToken(ref currentMessage)
                        : cursor.CurrentBlock!.Value.GetSequenceToken(cursor.Index, cacheDataAdapter); // 设定 cursor.State 时不中点 。
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
        /// 向缓存添加队列信件列表 
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
            // 从共享中分配信件
            CachedMessageBlock block = pool.AllocateMessage(message);

            // 如果新建块,如果新块,则在链接的列表中添加信息块
            if (block != messageBlocks.FirstOrDefault())
                messageBlocks.AddFirst(block.Node);
            ItemCount++;
        }

        /// <summary>
        /// 删除缓存中最老的信件, 如果块为空, 也删除最旧的块块
        /// </summary>
        public void RemoveOldestMessage()
        {
            TrackAndPurgeMetadata(this.messageBlocks.Last!.Value.OldestMessage); // 只有在缓存是空的时才打电话。

            this.messageBlocks.Last!.Value.Remove(); // 只有在缓存是空的时才打电话。
            this.ItemCount--;
            CachedMessageBlock lastCachedMessageBlock = this.messageBlocks.Last!.Value; // 只有在缓存是空的时才打电话。
            // 区块为空,但所有容量都已经过高,删除
            if (lastCachedMessageBlock.IsEmpty && !lastCachedMessageBlock.HasCapacity)
            {
                lastCachedMessageBlock.Dispose();
                this.messageBlocks.RemoveLast();
            }
        }

        private enum CursorStates
        {
            Unset, // 尚未确定或指出今后的一些数据。
            Set, // 点对缓存中信件的点
            Idle, // 已经对缓存中的所有相关事件进行了循环,正在等待关于流的更多数据。
            EarliestAvailableSet, // 以插入顺序选中的一条电文的点数供订阅使用,以便尽早获得。
            EarliestAvailableWaiting, // 等待下一个匹配流信息的实际缓存位置后再进行。
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

            // 当前序列符号; 取消设置时为空( 未确定序列符号)
            public StreamSequenceToken? SequenceToken;
            public long BlockGeneration;

            // 缓存中引用; 状态为 Set 时不停
            public LinkedListNode<CachedMessageBlock>? CurrentBlock;
            public int Index;

            // 水电公用事业费
            public bool IsEarliestAvailable => State is CursorStates.EarliestAvailableSet or CursorStates.EarliestAvailableWaiting;
            public bool IsNewestInBlock => Index == CurrentBlock!.Value.NewestMessageIndex; // 仅在状态设定时访问, 点 CurrentBlock 不中。
            public CachedMessage Message => CurrentBlock!.Value[Index]; // 仅在状态设定时访问, 点 CurrentBlock 不中。
        }
    }
}
