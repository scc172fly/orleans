using System.Collections.Immutable;
using Orleans.Runtime;
using Orleans.Runtime.GrainDirectory;

namespace Orleans.Configuration;

/// <summary>
/// 配置 grain 目录缓存和分区的选项。
/// </summary>
public class GrainDirectoryOptions
{
    /// <summary>
    /// 配置类型,用于控制 grain 目录缓存算法 silos 用途。
    /// </summary>
    public enum CachingStrategyType
    {
        /// <summary>别藏起来</summary>
        None,
        /// <summary>标准固定规模的LRU。</summary>
        LRU,
        /// <summary>适应性缓存,其最大尺寸和刷新为固定的固定大小。此选项应用于生产。</summary>
        [Obsolete("Adaptive caching is deprecated in favor of LRU and will be removed in a future version. This value is now an alias for LRU.")]
        Adaptive,
        /// <summary>自定义缓存实施,配置为在依赖性注射容器中登记 <see cref="IGrainDirectoryCache"/> 实施。</summary>
        Custom
    }

    /// <summary>
    /// 获取或设置要使用的缓存策略 。
    /// 选项为“无”, 这意味着不在当地缓存目录条目;
    /// 液态排减量,表明应使用标准固定规模的战略,最近最不使用;
    /// 适应性,这表明应采用一个具有固定最大规模的适应战略。
    /// 默认使用 LRU 战略。
    /// </summary>
    public CachingStrategyType CachingStrategy { get; set; } = DEFAULT_CACHING_STRATEGY;

    /// <summary>
    /// <see cref="CachingStrategy"/>的默认值。
    /// </summary>
    public const CachingStrategyType DEFAULT_CACHING_STRATEGY = CachingStrategyType.LRU;

    /// <summary>
    /// 获取或设定用于缓存目录信息的 grains 最大数量。
    /// </summary>
    public int CacheSize { get; set; } = DEFAULT_CACHE_SIZE;

    /// <summary>
    /// <see cref="CacheSize"/>的默认值。
    /// </summary>
    public const int DEFAULT_CACHE_SIZE = 1_000_000;

    /// <summary>
    /// 获取或设置每个 silo目录分区的数目。
    /// </summary>
    /// <remarks>
    /// 此选项仅适用于使用 <see cref="DistributedGrainDirectory"/>。
    /// 全部 silos 必须在滚动升级过程中使用相同的分区计数和 <see cref="GetPartitionBoundaries"/> 绘图。
    /// </remarks>
    public int PartitionsPerSilo { get; set; } = DEFAULT_PARTITIONS_PER_SILO;

    /// <summary>
    /// <see cref="PartitionsPerSilo"/>的默认值。
    /// </summary>
    public const int DEFAULT_PARTITIONS_PER_SILO = 1;

    /// <summary>
    /// 获取或设置函数,返回 silo 和分区数的目录分区边界。
    /// 每个数组指数都指向分区,其值是分区的独有起始边界。
    /// </summary>
    /// <remarks>
    /// 函数必须返回初始化的不可更改数组, 包括要求的边界数量
    /// 并在每 silo上绘制同样的绘图。
    /// 每个分界线的起始边界必须在整个 silo成员中保持稳定。
    /// 函数在分布目录创建时被记录,并可以在不同的 silos中同时引用。
    /// 如果函数抛弃或返回无效结果,目录会籍处理停止和会籍读作
    /// 并重新调用通知 <see cref="OrleansConfigurationException"/>。修改配置并重新启动受影响的 silos。
    /// 默认情况下,单一个分区使用 <see cref="SiloAddress.GetConsistentHashCode()"/>,多个分区使用
    /// <see cref="SiloAddress.GetUniformHashCodes(int)"/> 以大麻代序排列。
    /// </remarks>
    public Func<SiloAddress, int, ImmutableArray<uint>> GetPartitionBoundaries { get; set; } = DirectoryMembershipSnapshot.DefaultGetRingBoundaries;

    /// <summary>
    /// 以秒数获取或设定初始(最小)时间, 在重新验证前保留缓存条目 。
    /// </summary>
    [Obsolete("InitialCacheTTL is deprecated and will be removed in a future version.")]
    public TimeSpan InitialCacheTTL { get; set; } = DEFAULT_INITIAL_CACHE_TTL;

    /// <summary>
    /// <see cref="InitialCacheTTL"/>的默认值。
    /// </summary>
    [Obsolete("DEFAULT_INITIAL_CACHE_TTL is deprecated and will be removed in a future version.")]
    public static readonly TimeSpan DEFAULT_INITIAL_CACHE_TTL = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 获取或设定最大时间, 以秒计, 在重新确认前保留缓存条目 。
    /// </summary>
    public TimeSpan MaximumCacheTTL { get; set; } = DEFAULT_MAXIMUM_CACHE_TTL;

    /// <summary>
    /// <see cref="MaximumCacheTTL"/>的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MAXIMUM_CACHE_TTL = TimeSpan.FromSeconds(240);

    /// <summary>
    /// 获取或设定缓存条目 TTL 在发现稳定时延长的系数 。
    /// </summary>
    [Obsolete("CacheTTLExtensionFactor is deprecated and will be removed in a future version.")]
    public double CacheTTLExtensionFactor { get; set; } = DEFAULT_TTL_EXTENSION_FACTOR;

    /// <summary>
    /// <see cref="CacheTTLExtensionFactor"/>的默认值。
    /// </summary>
    [Obsolete("DEFAULT_TTL_EXTENSION_FACTOR is deprecated and will be removed in a future version.")]
    public const double DEFAULT_TTL_EXTENSION_FACTOR = 2.0;

    /// <summary>
    /// 获取或设定当我们为 grain 目录添加激活条目时与允许时间之间的时间间隔
    /// 有条件地删除该条目。
    /// 有条件的注销登记用于懒惰地清理由于某种原因未能及时取消登记的启动(e.g.,电文故障)。
    /// 这应该总是至少一分钟, 因为比较目录分区的时间, 所以信件延迟和 clacks skues 已经
    /// 允许。
    /// </summary>
    public TimeSpan LazyDeregistrationDelay { get; set; } = DEFAULT_UNREGISTER_RACE_DELAY;

    /// <summary>
    /// <see cref="LazyDeregistrationDelay"/>的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_UNREGISTER_RACE_DELAY = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 在括号 silo 故障后,获取或设定适用的时间租赁期限。
    /// 这一期限适用于两种设想情景:
    /// <list type="bullet">
    /// <item>当特定 silo 漫步的碰撞 grain 租赁合同的持有者不得在 grains 持续到此期间。</item>
    /// <item>当目录分区无法从先前的所有人处获得快照时, 范围租赁会阻止在这一期限内在该范围内进行新的登记。</item>
    /// </list>
    /// 对于已宣布死亡的 silo,租赁从估计失败时间开始,从申报时间减去配置的故障检测窗口。
    /// 将此值设为 <see cref="TimeSpan.Zero"/> ,以禁用范围租赁。
    /// </summary>
    public TimeSpan RangeLeaseDuration { get; set; } = DEFAULT_RANGE_LEASE_DURATION;

    /// <summary>
    /// <see cref="RangeLeaseDuration"/>的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RANGE_LEASE_DURATION = TimeSpan.FromSeconds(30);
}
