
namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 堆积着紧凑的缓存信息,这些封存在大块块中,以减少GC的压力。
    /// </summary>
    internal class CachedMessagePool
    {
        private readonly IObjectPool<CachedMessageBlock> messagePool;
        private CachedMessageBlock? currentMessageBlock;

        /// <summary>
        /// 分配一个隐藏的邮件区块库。
        /// </summary>
        /// <param name="cacheDataAdapter">缓存数据适配器。</param>
        public CachedMessagePool(ICacheDataAdapter cacheDataAdapter)
        {
            messagePool = new ObjectPool<CachedMessageBlock>(
                () => new CachedMessageBlock());
        }

        /// <summary>
        /// 在块中分配信件, 并返回消息所在的块。
        /// </summary>
        /// <returns>信件所分配的缓存信息块。</returns>
        public CachedMessageBlock AllocateMessage(CachedMessage message)
        {
            CachedMessageBlock returnBlock = currentMessageBlock ?? (currentMessageBlock = messagePool.Allocate());
            returnBlock.Add(message);

            // 能力上 的街区是可用于清洗的 所以我们不想被它们控制
            if (!currentMessageBlock.HasCapacity)
            {
                currentMessageBlock = messagePool.Allocate();
            }

            return returnBlock;
        }
    }
}
