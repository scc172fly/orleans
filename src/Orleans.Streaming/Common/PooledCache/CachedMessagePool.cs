
namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 紧凑存储的缓存消息池，消息保存在较大的块中以减轻 GC 压力。
    /// </summary>
    internal class CachedMessagePool
    {
        private readonly IObjectPool<CachedMessageBlock> messagePool;
        private CachedMessageBlock? currentMessageBlock;

        /// <summary>
        /// 分配缓存消息块池。
        /// </summary>
        /// <param name="cacheDataAdapter">缓存数据适配器。</param>
        public CachedMessagePool(ICacheDataAdapter cacheDataAdapter)
        {
            messagePool = new ObjectPool<CachedMessageBlock>(
                () => new CachedMessageBlock());
        }

        /// <summary>
        /// 在块中分配一条消息，并返回该消息所在的块。
        /// </summary>
        /// <returns>分配该消息时使用的缓存消息块。</returns>
        public CachedMessageBlock AllocateMessage(CachedMessage message)
        {
            CachedMessageBlock returnBlock = currentMessageBlock ?? (currentMessageBlock = messagePool.Allocate());
            returnBlock.Add(message);

            // 已满的块可以被清除，因此不应继续持有这些块。
            if (!currentMessageBlock.HasCapacity)
            {
                currentMessageBlock = messagePool.Allocate();
            }

            return returnBlock;
        }
    }
}
