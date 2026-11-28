namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪内存块池相关指标的监视器。缓存系统使用内存块池进行内存管理。
    /// </summary>
    public interface IBlockPoolMonitor
    {
        /// <summary>
        /// 当缓存新分配内存时调用。
        /// </summary>
        /// <param name="allocatedMemoryInBytes">已分配的内存（字节）。</param>
        void TrackMemoryAllocated(long allocatedMemoryInBytes);

        /// <summary>
        /// 当缓存释放内存时调用。
        /// </summary>
        /// <param name="releasedMemoryInBytes">已释放的内存（字节）。</param>
        void TrackMemoryReleased(long releasedMemoryInBytes);

        /// <summary>
        /// 定期报告内存块池状态。
        /// </summary>
        /// <param name="totalSizeInByte">此内存块池分配的总内存。</param>
        /// <param name="availableMemoryInByte">可分配给缓存的内存。</param>
        /// <param name="claimedMemoryInByte">缓存正在使用的内存。</param>
        void Report(long totalSizeInByte, long availableMemoryInByte, long claimedMemoryInByte);
    }

    /// <summary>
    /// ObjectPoolMonitor 按对象数量报告 ObjectPool 的指标；BlockPoolMonitor 按内存大小报告 BlockPool 的指标。
    /// 这两种监视器在 Orleans 缓存基础设施中汇合：ObjectPool 用作内存块池，每个对象表示一块
    /// 具有一定大小的内存。ObjectPoolMonitorBridge 在缓存基础设施中桥接两种监视器。当 ObjectPoolMonitor 报告指标时，
    /// 用户配置的 BlockPoolMonitor 将调用对应方法，并按公式 memoryInByte = objectCount*objectSizeInByte 报告指标。
    /// </summary>
    public class ObjectPoolMonitorBridge : IObjectPoolMonitor
    {
        private readonly IBlockPoolMonitor blockPoolMonitor;
        private readonly int blockSizeInBytes;

        /// <summary>
        /// 初始化 <see cref="ObjectPoolMonitorBridge"/> 类的新实例。
        /// </summary>
        /// <param name="blockPoolMonitor">内存块池监视器。</param>
        /// <param name="blockSizeInBytes">块大小（字节）。</param>
        public ObjectPoolMonitorBridge(IBlockPoolMonitor blockPoolMonitor, int blockSizeInBytes)
        {
            this.blockPoolMonitor = blockPoolMonitor;
            this.blockSizeInBytes = blockSizeInBytes;
        }

        /// <inheritdoc />
        public void TrackObjectAllocated()
        {
            long memoryAllocatedInByte = blockSizeInBytes;
            this.blockPoolMonitor.TrackMemoryAllocated(memoryAllocatedInByte);
        }

        /// <inheritdoc />
        public void TrackObjectReleased()
        {
            long memoryReleasedInByte = blockSizeInBytes;
            this.blockPoolMonitor.TrackMemoryReleased(memoryReleasedInByte);
        }

        /// <inheritdoc />
        public void Report(long totalObjects, long availableObjects, long claimedObjects)
        {
            var totalMemoryInByte = totalObjects * this.blockSizeInBytes;
            var availableMemoryInByte = availableObjects * this.blockSizeInBytes;
            var claimedMemoryInByte = claimedObjects * this.blockSizeInBytes;
            this.blockPoolMonitor.Report(totalMemoryInByte, availableMemoryInByte, claimedMemoryInByte);
        }
    }
}
