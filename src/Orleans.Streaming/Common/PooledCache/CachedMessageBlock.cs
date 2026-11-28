
using System;
using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// CachedMessageBlock 是一组紧凑存储的结构，包含缓存消息的跟踪数据。
    /// 这些数据紧凑排列以减轻 GC 压力；队列缓存使用这些跟踪数据遍历缓存，
    /// 按流提供有序的队列消息。
    /// </summary>
    public class CachedMessageBlock : PooledResource<CachedMessageBlock>
    {
        private const int OneKb = 1024;
        private const int DefaultCachedMessagesPerBlock = 16 * OneKb; // 16 KB（译注：此常量实际表示 16 × 1024 条消息的数量。）

        private readonly CachedMessage[] cachedMessages;
        private readonly int blockSize;
        private int writeIndex;
        private int readIndex;
        private long generation;

        /// <summary>
        /// 链表节点，使此消息块可以保存在链表中。
        /// </summary>
        public LinkedListNode<CachedMessageBlock> Node { get; private set; }

        /// <summary>
        /// 获取一个值，指示是否可以向此块添加更多消息。
        /// </summary>
        public bool HasCapacity => writeIndex < blockSize;

        /// <summary>
        /// 获取一个值，指示此块是否为空。
        /// </summary>
        public bool IsEmpty => readIndex >= writeIndex;

        /// <summary>
        /// 获取最近添加到此块的消息的索引。
        /// </summary>
        public int NewestMessageIndex => writeIndex - 1;

        /// <summary>
        /// 获取此块中最早消息的索引。
        /// </summary>
        public int OldestMessageIndex => readIndex;

        internal int WriteIndex => writeIndex;

        internal long Generation => generation;

        /// <summary>
        /// 获取此块中最早的消息。
        /// </summary>
        public CachedMessage OldestMessage => cachedMessages[OldestMessageIndex];

        /// <summary>
        /// 获取此块中最新的消息。
        /// </summary>
        public CachedMessage NewestMessage => cachedMessages[NewestMessageIndex];

        /// <summary>
        /// 获取此块中的消息数量。
        /// </summary>
        public int ItemCount
        {
            get
            {
                int count = writeIndex - readIndex;
                return count >= 0 ? count : 0;
            }
        }

        /// <summary>
        /// 缓存消息块。
        /// </summary>
        /// <param name="blockSize">块大小，以消息数量表示。</param>
        public CachedMessageBlock(int blockSize = DefaultCachedMessagesPerBlock)
        {
            this.blockSize = blockSize;
            cachedMessages = new CachedMessage[blockSize];
            writeIndex = 0;
            readIndex = 0;
            Node = new LinkedListNode<CachedMessageBlock>(this);
        }

        /// <summary>
        /// 从块的起始位置移除一条消息（最早的数据）。
        /// </summary>
        /// <returns>如果仍有剩余数据项，则为 <see langword="true"/>；否则为 <see langword="false"/>。（译注：实现返回是否成功移除消息；移除最后一条时也返回 true。）</returns>
        public bool Remove()
        {
            if (readIndex < writeIndex)
            {
                cachedMessages[readIndex] = default;
                readIndex++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 将队列中的一条消息添加到此块。
        /// 将队列消息转换为缓存消息，并存储在此块的末尾。
        /// </summary>        
        /// <param name="message">
        /// 要添加到此块的消息。
        /// </param>
        public void Add(CachedMessage message)
        {
            if (!HasCapacity)
            {
                throw new InvalidOperationException("Block is full");
            }

            int index = writeIndex++;
            cachedMessages[index] = message;
        }

        /// <summary>
        /// 访问指定索引处的缓存消息。
        /// </summary>
        /// <param name="index">要访问的索引。</param>
        /// <returns>指定索引处的消息。</returns>
        public CachedMessage this[int index]
        {
            get
            {
                if (index >= writeIndex || index < readIndex)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return cachedMessages[index];
            }
        }

        /// <summary>
        /// 获取指定索引处缓存消息的序列令牌。
        /// </summary>
        /// <param name="index">要访问的消息的索引。</param>
        /// <param name="dataAdapter">数据适配器。</param>
        /// <returns>序列令牌。</returns>
        public StreamSequenceToken GetSequenceToken(int index, ICacheDataAdapter dataAdapter)
        {
            if (index >= writeIndex || index < readIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return dataAdapter.GetSequenceToken(ref cachedMessages[index]);
        }

        internal bool Contains(int index, long expectedGeneration)
            => index >= readIndex
                && index < writeIndex
                && generation == expectedGeneration;

        /// <summary>
        /// 获取此块中最新消息的序列令牌。
        /// </summary>
        /// <param name="dataAdapter">数据适配器。</param>
        /// <returns>此块中最新消息的序列令牌。</returns>
        public StreamSequenceToken GetNewestSequenceToken(ICacheDataAdapter dataAdapter)
        {
            return GetSequenceToken(NewestMessageIndex, dataAdapter);
        }

        /// <summary>
        /// 获取此块中最早消息的序列令牌。
        /// </summary>
        /// <param name="dataAdapter">数据适配器。</param>
        /// <returns>此块中最早消息的序列令牌。</returns>
        public StreamSequenceToken GetOldestSequenceToken(ICacheDataAdapter dataAdapter)
        {
            return GetSequenceToken(OldestMessageIndex, dataAdapter);
        }

        /// <summary>
        /// 获取此块中第一条序列令牌不晚于指定令牌的消息的索引。
        /// </summary>
        /// <param name="token">序列令牌。</param>
        /// <returns>此块中第一条序列令牌等于或早于指定令牌的消息的索引。</returns>
        public int GetIndexOfFirstMessageLessThanOrEqualTo(StreamSequenceToken token)
        {
            for (int i = writeIndex - 1; i >= readIndex; i--)
            {
                if (cachedMessages[i].Compare(token) <= 0)
                {
                    return i;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        /// <summary>
        /// 获取此块中序列令牌小于或等于指定令牌的最新消息的索引。
        /// </summary>
        /// <param name="token">序列令牌。</param>
        /// <param name="dataAdapter">用于比较提供程序特定位置的数据适配器。</param>
        /// <returns>序列令牌小于或等于指定令牌的最新消息的索引。</returns>
        public int GetIndexOfFirstMessageLessThanOrEqualTo(
            StreamSequenceToken token,
            ICacheDataAdapter dataAdapter)
        {
            ArgumentNullException.ThrowIfNull(dataAdapter);
            for (int i = writeIndex - 1; i >= readIndex; i--)
            {
                if (dataAdapter.Compare(ref cachedMessages[i], token) <= 0)
                {
                    return i;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(token));
        }

        /// <summary>
        /// 尝试查找此块中属于指定流的第一条消息。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="dataAdapter">数据适配器。</param>
        /// <param name="index">索引。</param>
        /// <returns>如果找到消息，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        public bool TryFindFirstMessage(StreamId streamId, ICacheDataAdapter dataAdapter, out int index)
        {
            return TryFindNextMessage(readIndex, streamId, dataAdapter, out index);
        }

        /// <summary>
        /// 从起始索引开始，尝试获取指定流中的下一条消息。
        /// </summary>        
        /// <param name="start">起始索引。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="dataAdapter">数据适配器。</param>
        /// <param name="index">索引。</param>
        /// <returns>如果找到消息，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        public bool TryFindNextMessage(int start, StreamId streamId, ICacheDataAdapter dataAdapter, out int index)
        {
            if (start < readIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            for (int i = start; i < writeIndex; i++)
            {
                if (cachedMessages[i].CompareStreamId(streamId))
                {
                    index = i;
                    return true;
                }
            }

            index = writeIndex - 1;
            return false;
        }

        /// <inheritdoc/>
        public override void OnResetState()
        {
            Array.Clear(cachedMessages, 0, writeIndex);
            writeIndex = 0;
            readIndex = 0;
            generation++;
        }
    }
}
