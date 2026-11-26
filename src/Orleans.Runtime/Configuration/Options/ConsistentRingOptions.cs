namespace Orleans.Configuration
{
    /// <summary>
    /// 用于平衡整个组群资源分配的一致散列算法配置选项。
    /// </summary>
    public class ConsistentRingOptions
    {
        /// <summary>
        /// 在一个前后一致的散列环中获取或设置 silo 所保持的登记登记数。
        ///   更多的虚拟桶增加了平衡均衡的可能性
        ///   管理费用略有增加。 
        /// </summary>
        public int NumVirtualBucketsConsistentRing { get; set; } = DEFAULT_NUM_VIRTUAL_RING_BUCKETS;

        /// <summary>
        /// 虚拟环桶的默认数量 。
        /// </summary>
        public const int DEFAULT_NUM_VIRTUAL_RING_BUCKETS = 30;

        /// <summary>
        /// 获取或设定一个数值,表明是否启用虚拟桶。
        /// </summary>
        public bool UseVirtualBucketsConsistentRing { get; set; } = DEFAULT_USE_VIRTUAL_RING_BUCKETS;

        /// <summary>
        /// <see cref="UseVirtualBucketsConsistentRing"/>的默认值。
        /// </summary>
        public const bool DEFAULT_USE_VIRTUAL_RING_BUCKETS = true;
    }
}
