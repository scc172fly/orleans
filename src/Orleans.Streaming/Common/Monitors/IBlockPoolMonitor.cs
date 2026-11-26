namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 与数据库有关的指标。 
    /// </summary>
    public interface IBlockPoolMonitor
    {
        /// <summary>
        /// 当缓存新分配内存时调用 。
        /// </summary>
        /// <param name="allocatedMemoryInBytes">分配的内存,以字节表示。</param>
        void TrackMemoryAllocated(long allocatedMemoryInBytes);

        /// <summary>
        /// 缓存释放记忆时呼叫
        /// </summary>
        /// <param name="releasedMemoryInBytes">释放的内存,以字节表示。</param>
        void TrackMemoryReleased(long releasedMemoryInBytes);

        /// <summary>
        /// 定期报告集体现金池状况
        /// </summary>
        /// <param name="totalSizeInByte">全部内存这个分配的区块集合。</param>
        /// 可用于分配缓存的<param name="availableMemoryInByte">内存。</param>
        /// <param name="claimedMemoryInByte">内存被缓存使用。</param>
        void Report(long totalSizeInByte, long availableMemoryInByte, long claimedMemoryInByte);
    }

    /// <summary>
    /// ObjectPoolMonitor 报告 ObjectPool的度量,这些度量以天体数为基础。 BlockPoolMonitor 报告 BlockPool的度量,根据内存大小。 
    /// 这两个监视器在 orleans 缓存基础设施中汇合,其中 ObjectPool 被用作分配内存的块状集合,每个对象代表一个内存块
    /// 其大小为. ObjectPoolMonitorBridge 是这两个监测器在缓存基础设施中的桥梁。当 ObjectPoolMonitor 报告一个度时, 
    /// 用户配置的 BlockPoolMonitor 将调用对应的方法,并根据数学报告: memoryInByte = objectCount*objectSizeInByte
    /// </summary>
    public class ObjectPoolMonitorBridge : IObjectPoolMonitor
    {
        private readonly IBlockPoolMonitor blockPoolMonitor;
        private readonly int blockSizeInBytes;

        /// <summary>
        /// 初始化 <see cref="ObjectPoolMonitorBridge"/> 类的新实例。
        /// </summary>
        /// <param name="blockPoolMonitor">块集合监视器。</param>
        /// <param name="blockSizeInBytes">区块大小以字节为单位。</param>
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
