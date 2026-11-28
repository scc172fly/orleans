using System.Collections.Immutable;
using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;

namespace Orleans.Configuration;

/// <summary>
/// 用于配置 Grain 目录缓存和分区的选项。
/// </summary>
public class GrainDirectoryOptions
{
    /// <summary>
    /// 控制 Silo 使用哪种 Grain 目录缓存算法的配置类型。
    /// </summary>
    public enum CachingStrategyType
    {
        /// <summary>不使用缓存。</summary>
        None,
        /// <summary>标准的固定大小 LRU 缓存。</summary>
        LRU,
        /// <summary>具有固定最大容量和刷新机制的自适应缓存。生产环境应使用此选项。</summary>
        [Obsolete("Adaptive caching is deprecated in favor of LRU and will be removed in a future version. This value is now an alias for LRU.")]
        Adaptive,
        /// <summary>自定义缓存实现，通过在依赖注入容器中注册 <see cref="IGrainDirectoryCache"/> 的实现进行配置。</summary>
        Custom
    }

    /// <summary>
    /// 获取或设置要使用的缓存策略。
    /// 可选值包括 None，表示不在本地缓存目录条目；
    /// LRU，表示使用标准的固定容量最近最少使用策略；以及
    /// Adaptive，表示使用具有固定最大容量的自适应策略。
    /// 默认使用 LRU 策略。
    /// </summary>
    public CachingStrategyType CachingStrategy { get; set; } = DEFAULT_CACHING_STRATEGY;

    /// <summary>
    /// <see cref="CachingStrategy"/> 的默认值。
    /// </summary>
    public const CachingStrategyType DEFAULT_CACHING_STRATEGY = CachingStrategyType.LRU;

    /// <summary>
    /// 获取或设置可缓存目录信息的最大 Grain 数量。
    /// </summary>
    public int CacheSize { get; set; } = DEFAULT_CACHE_SIZE;

    /// <summary>
    /// <see cref="CacheSize"/> 的默认值。
    /// </summary>
    public const int DEFAULT_CACHE_SIZE = 1_000_000;

    /// <summary>
    /// 获取或设置每个 Silo 的目录分区数。
    /// </summary>
    /// <remarks>
    /// 仅当使用 <see cref="DistributedGrainDirectory"/> 时此选项才适用。
    /// 在整个滚动升级期间，所有 Silo 必须使用相同的分区数和 <see cref="GetPartitionBoundaries"/> 映射。
    /// </remarks>
    public int PartitionsPerSilo { get; set; } = DEFAULT_PARTITIONS_PER_SILO;

    /// <summary>
    /// <see cref="PartitionsPerSilo"/> 的默认值。
    /// </summary>
    public const int DEFAULT_PARTITIONS_PER_SILO = 1;

    /// <summary>
    /// 获取或设置根据 Silo 和分区数返回目录分区边界的函数。
    /// 数组的每个索引标识一个分区，其值是该分区不包含在内的起始边界。
    /// </summary>
    /// <remarks>
    /// 此函数必须返回已初始化的不可变数组，其中恰好包含所请求数量的边界，
    /// 并在每个 Silo 上生成相同的映射。
    /// 在 Silo 的整个成员资格存续期间，每个分区的起始边界必须保持稳定。
    /// 创建分布式目录时会捕获此函数；不同 Silo 可能并发调用它。
    /// 如果函数抛出异常或返回无效结果，目录成员资格处理将停止，成员资格读取
    /// 和刷新调用将抛出 <see cref="OrleansConfigurationException"/>。请修正配置并重启受影响的 Silo。
    /// 默认情况下，单个分区使用 <see cref="SiloAddress.GetConsistentHashCode()"/>，多个分区使用
    /// <see cref="SiloAddress.GetUniformHashCodes(int)"/>，按哈希生成顺序排列。
    /// </remarks>
    public Func<SiloAddress, int, ImmutableArray<uint>> GetPartitionBoundaries { get; set; } = DirectoryMembershipSnapshot.DefaultGetRingBoundaries;

    /// <summary>
    /// 获取或设置重新验证之前保留缓存条目的初始（最短）时间，单位为秒。
    /// </summary>
    [Obsolete("InitialCacheTTL is deprecated and will be removed in a future version.")]
    public TimeSpan InitialCacheTTL { get; set; } = DEFAULT_INITIAL_CACHE_TTL;

    /// <summary>
    /// <see cref="InitialCacheTTL"/> 的默认值。
    /// </summary>
    [Obsolete("DEFAULT_INITIAL_CACHE_TTL is deprecated and will be removed in a future version.")]
    public static readonly TimeSpan DEFAULT_INITIAL_CACHE_TTL = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 获取或设置重新验证之前保留缓存条目的最长时间，单位为秒。
    /// </summary>
    public TimeSpan MaximumCacheTTL { get; set; } = DEFAULT_MAXIMUM_CACHE_TTL;

    /// <summary>
    /// <see cref="MaximumCacheTTL"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MAXIMUM_CACHE_TTL = TimeSpan.FromSeconds(240);

    /// <summary>
    /// 获取或设置缓存条目被确认稳定时延长其 TTL 的倍数。
    /// </summary>
    [Obsolete("CacheTTLExtensionFactor is deprecated and will be removed in a future version.")]
    public double CacheTTLExtensionFactor { get; set; } = DEFAULT_TTL_EXTENSION_FACTOR;

    /// <summary>
    /// <see cref="CacheTTLExtensionFactor"/> 的默认值。
    /// </summary>
    [Obsolete("DEFAULT_TTL_EXTENSION_FACTOR is deprecated and will be removed in a future version.")]
    public const double DEFAULT_TTL_EXTENSION_FACTOR = 2.0;

    /// <summary>
    /// 获取或设置从向 Grain 目录添加激活实例条目开始，直到允许
    /// 有条件地移除该条目为止的时间间隔。
    /// 条件注销用于延迟清理因某些原因（例如消息失败）而未能及时注销的激活实例。
    /// 此时间始终应至少为一分钟，因为时间是在目录分区上比较的，所以必须允许消息延迟及
    /// 时钟偏差。
    /// </summary>
    public TimeSpan LazyDeregistrationDelay { get; set; } = DEFAULT_UNREGISTER_RACE_DELAY;

    /// <summary>
    /// <see cref="LazyDeregistrationDelay"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_UNREGISTER_RACE_DELAY = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置 Silo 非正常故障后应用的范围租约时长。
    /// 此时长适用于两种情况：
    /// <list type="bullet">
    /// <item>当特定 Silo 非正常崩溃时，Grain 租约保留机制在此时长内阻止其各个 Grain 重新注册。</item>
    /// <item>当目录分区无法从先前的所有者获取快照时，范围租约保留机制在此时长内阻止整个范围内的新注册。</item>
    /// </list>
    /// 对于已被宣告死亡的 Silo，租约从其估计故障时间开始，即宣告时间减去配置的故障检测窗口。
    /// 将此值设为 <see cref="TimeSpan.Zero"/> 可禁用范围租约。
    /// </summary>
    public TimeSpan RangeLeaseDuration { get; set; } = DEFAULT_RANGE_LEASE_DURATION;

    /// <summary>
    /// <see cref="RangeLeaseDuration"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RANGE_LEASE_DURATION = TimeSpan.FromSeconds(30);
}
