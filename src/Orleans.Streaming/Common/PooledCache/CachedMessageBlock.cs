
using System;
using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// CachedMessageBlock 是一组密布的紧固结构块, 内有缓存信件跟踪数据。 此数据是 
    ///   紧紧地打包到降低的GC压力。跟踪数据被队列缓存用于随行按顺序提供的缓存
    ///   按流序排列队列信息 。
    /// </summary>
    public class CachedMessageBlock : PooledResource<CachedMessageBlock>
    {
        private const int OneKb = 1024;
        private const int DefaultCachedMessagesPerBlock = 16 * OneKb; // 16千b

        private readonly CachedMessage[] cachedMessages;
        private readonly int blockSize;
        private int writeIndex;
        private int readIndex;
        private long generation;

        /// <summary>
        /// 链接列表节点, 因此此信件块可以保存在链接的列表中 。
        /// </summary>
        public LinkedListNode<CachedMessageBlock> Node { get; private set; }

        /// <summary>
        /// 获得一个值, 显示是否可以向块中添加更多信件 。
        /// </summary>
        public bool HasCapacity => writeIndex < blockSize;

        /// <summary>
        /// 获得一个值,以显示此块是否为空。
        /// </summary>
        public bool IsEmpty => readIndex >= writeIndex;

        /// <summary>
        /// 获取加入区块的最新信件索引 。
        /// </summary>
        public int NewestMessageIndex => writeIndex - 1;

        /// <summary>
        /// 获取此街区中最古老信息索引 。
        /// </summary>
        public int OldestMessageIndex => readIndex;

        internal int WriteIndex => writeIndex;

        internal long Generation => generation;

        /// <summary>
        /// 获得最古老的信息 在街区。
        /// </summary>
        public CachedMessage OldestMessage => cachedMessages[OldestMessageIndex];

        /// <summary>
        /// 获得这个街区的最新消息。
        /// </summary>
        public CachedMessage NewestMessage => cachedMessages[NewestMessageIndex];

        /// <summary>
        /// 获取此街区的留言数 。
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
        /// 缓存信件块 。
        /// </summary>
        /// <param name="blockSize">块大小, 以若干信息表示 。</param>
        public CachedMessageBlock(int blockSize = DefaultCachedMessagesPerBlock)
        {
            this.blockSize = blockSize;
            cachedMessages = new CachedMessage[blockSize];
            writeIndex = 0;
            readIndex = 0;
            Node = new LinkedListNode<CachedMessageBlock>(this);
        }

        /// <summary>
        /// 从块的开头删除信件( 最新数据) 。
        /// </summary>
        /// <returns><see langword="true"/> 如果还有更多剩余项目,否则 <see langword="false"/>。</returns>
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
        /// 将队列中的一条消息添加到块中 。
        /// 将队列信件转换为缓存信件,并将其保存在块的末尾。
        /// </summary>        
        /// <param name="message">
        /// 要添加到此块的邮件 。
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
        /// 访问提供索引处的缓存信件 。
        /// </summary>
        /// <param name="index">访问索引。</param>
        /// <returns>指定索引处的电文。</returns>
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
        /// 获取缓存信件的序列符号
        /// </summary>
        /// <param name="index">访问信件的索引。</param>
        /// <param name="dataAdapter">数据适配器</param>
        /// <returns>序列符号。</returns>
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
        /// 获取此块中最新信件的序列符号
        /// </summary>
        /// <param name="dataAdapter">数据适配器</param>
        /// <returns>此块中最新信件的序列符号 。</returns>
        public StreamSequenceToken GetNewestSequenceToken(ICacheDataAdapter dataAdapter)
        {
            return GetSequenceToken(NewestMessageIndex, dataAdapter);
        }

        /// <summary>
        /// 获取此块中最古老信件的序列符号
        /// </summary>
        /// <param name="dataAdapter">数据适配器</param>
        /// <returns>此块中最古老信件的序列符号。</returns>
        public StreamSequenceToken GetOldestSequenceToken(ICacheDataAdapter dataAdapter)
        {
            return GetSequenceToken(OldestMessageIndex, dataAdapter);
        }

        /// <summary>
        /// 获取此块中第一个信件的索引,该消息在所提供的符号上或之前有一个序列符号
        /// </summary>
        /// <param name="token">序列符号。</param>
        /// <returns>此块中第一个信件的索引, 该信头中的序列符号与所提供的符号相同或之前。</returns>
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
        /// 获取此块中序列符号小于或等于所提供标记的最新信件的索引。
        /// </summary>
        /// <param name="token">序列符号。</param>
        /// <param name="dataAdapter">用于比较供应商特定位置的数据适配器。</param>
        /// <returns>序列符号小于或等于所提供标记的最新信件索引。</returns>
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
        /// 尝试在块中找到第一条信息,这是所提供流的一部分。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="dataAdapter">数据适配器</param>
        /// <param name="index">索引。</param>
        /// <returns><see langword="true" /> 如果发现消息, <see langword="false" /> 否则。</returns>
        public bool TryFindFirstMessage(StreamId streamId, ICacheDataAdapter dataAdapter, out int index)
        {
            return TryFindNextMessage(readIndex, streamId, dataAdapter, out index);
        }

        /// <summary>
        /// 尝试从提供的流获取下一条消息, 从开始索引开始 。
        /// </summary>        
        /// <param name="start">起始指数。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="dataAdapter">数据适配器</param>
        /// <param name="index">索引。</param>
        /// <returns><see langword="true" /> 如果发现消息, <see langword="false" /> 否则。</returns>
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
