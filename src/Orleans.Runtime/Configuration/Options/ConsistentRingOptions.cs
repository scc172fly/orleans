namespace Orleans.Configuration
{
    /// <summary>
    /// 一致性哈希算法的配置选项，用于在集群中均衡资源分配。
    /// </summary>
    public class ConsistentRingOptions
    {
        /// <summary>
        /// 获取或设置 Silo 在一致性哈希环上维护的注册项数。这会影响集群资源分配的概率性
        /// 均衡效果。更多虚拟桶可提高均匀分配的概率，
        /// 同时仅小幅增加管理成本。
        /// </summary>
        public int NumVirtualBucketsConsistentRing { get; set; } = DEFAULT_NUM_VIRTUAL_RING_BUCKETS;

        /// <summary>
        /// 虚拟环桶的默认数量。
        /// </summary>
        public const int DEFAULT_NUM_VIRTUAL_RING_BUCKETS = 30;

        /// <summary>
        /// 获取或设置是否启用虚拟桶的值。
        /// </summary>
        public bool UseVirtualBucketsConsistentRing { get; set; } = DEFAULT_USE_VIRTUAL_RING_BUCKETS;

        /// <summary>
        /// <see cref="UseVirtualBucketsConsistentRing"/> 的默认值。
        /// </summary>
        public const bool DEFAULT_USE_VIRTUAL_RING_BUCKETS = true;
    }
}
