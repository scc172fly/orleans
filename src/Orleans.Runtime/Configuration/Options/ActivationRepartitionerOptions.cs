using System;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 配置激活重新分割的选项 。
/// </summary>
public sealed class ActivationRepartitionerOptions
{
    /// <summary>
    /// <para>
    /// 重新分割回合中保留记忆中的边缘的最大边数。 边缘代表从一个 grain 对另一个。
    /// </para>
    /// <para>
    /// 如果这个数字是N,则并不意味着N启动将在重新分配后迁移。
    /// 这也不意味着任何启动的排名都很高,在下一个周期中将排位高。
    /// 在最极端的情况下, 迁移的激活次数将等于这个数目, 这样你就可以知道为此设定一个合理的价值。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 为了保存记忆,最重的链接是概率性记录,因此存在一个与此相关的内在错误。
    /// 此错误与此值成反比, 因此不推荐低于100的数值。 如果您注意到系统没有快速融合, 请考虑增加这个数字 。
    /// </remarks>
    public int MaxEdgeCount { get; set; } = DEFAULT_MAX_EDGE_COUNT;

    /// <summary>
    /// 默认值 <see cref="MaxEdgeCount"/>。
    /// </summary>
    public const int DEFAULT_MAX_EDGE_COUNT = 10_000;

    /// <summary>
    /// 启动再分配回合的最小时间 。
    /// </summary>
    /// <remarks>实际到期时间是随机选择的,在 <see cref="MaxRoundPeriod"/>.</remarks>
    public TimeSpan MinRoundPeriod { get; set; } = DEFAULT_MINUMUM_ROUND_PERIOD;

    /// <summary>
    /// 默认值 <see cref="MinRoundPeriod"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MINUMUM_ROUND_PERIOD = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 启动再分配回合之间的最大时间 。
    /// </summary>
    /// <remarks>
    /// <para>实际到期时间是随机选择的,在 <see cref="MinRoundPeriod"/>.</para>
    /// <para>为了取得最佳效果,您应力求将这一额外增加10秒乘以预期的上限 silo 组合中计数。</para>
    /// </remarks>
    public TimeSpan MaxRoundPeriod { get; set; } = DEFAULT_MAXIMUM_ROUND_PERIOD;

    /// <summary>
    /// 默认值 <see cref="MaxRoundPeriod"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MAXIMUM_ROUND_PERIOD = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 上一轮再分配后 silo 恢复的最低时间。
    /// 直到这一时间过去,这个 silo 将不参加任何其他 silo的企图。
    /// </summary>
    public TimeSpan RecoveryPeriod { get; set; } = DEFAULT_RECOVERY_PERIOD;

    /// <summary>
    /// 默认值 <see cref="RecoveryPeriod"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RECOVERY_PERIOD = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 缓冲处未处理边缘的最大数量。 如果超过此数量, 则会丢弃最老的边缘 。
    /// </summary>
    public int MaxUnprocessedEdges { get; set; } = DEFAULT_MAX_UNPROCESSED_EDGES;

    /// <summary>
    /// 默认值 <see cref="MaxUnprocessedEdges"/>。
    /// </summary>
    public const int DEFAULT_MAX_UNPROCESSED_EDGES = 100_000;

    /// <summary>
    /// 获取或设置值,以显示是否启用本地顶点过滤器。此过滤器跟踪
    /// 脊椎是分很细的(从当地宿主移走会有害),并粉碎
    /// 成单数 /silo 减少跟踪涉及该顶端的边缘所需的空间的顶部。 结果
    /// 准确性降低,但有可能大大提高重新分配者的效力,因为
    /// 分布良好的边缘不会主宰顶层K级数据结构,留有足够的跟踪空间
    /// 不带完全分隔的顶点。 这是默认启用的 。
    /// </summary>
    public bool AnchoringFilterEnabled { get; set; } = DEFAULT_ANCHORING_FILTER_ENABLED;

    /// <summary>
    /// 默认值 <see cref="AnchoringFilterEnabled"/>。
    /// </summary>
    public const bool DEFAULT_ANCHORING_FILTER_ENABLED = true;

    /// <summary>
    /// <see cref="AnchoringFilterEnabled"/> 设为 <see langword="true"/>时允许的最大错误率,否则此值不适用。
    /// </summary>
    /// <remarks>允许范围: [0.001 - 0.01](0.1% - 1%)</remarks>
    public double ProbabilisticFilteringMaxAllowedErrorRate { get; set; } = DEFAULT_PROBABILISTIC_FILTERING_MAX_ALLOWED_ERROR;

    /// <summary>
    /// 默认值 <see cref="ProbabilisticFilteringMaxAllowedErrorRate"/>。
    /// </summary>
    public const double DEFAULT_PROBABILISTIC_FILTERING_MAX_ALLOWED_ERROR = 0.01d;

    /// <summary>
    /// <para>
    /// 确定保存锁定历史的时间长度。低代数可以让过滤器更新,但可能会忘记锁定 grains 的过快。
    /// 高一代的统计保留了更长的历史,但增加了记忆的使用和饱和的风险。
    /// </para>
    /// <para>
    /// 因为过滤器每轮的分化周期都精确衰减一次, 实际“冷” grain 固定在地上的时间直接与 
    /// <see cref="MinRoundPeriod"/> 和 <see cref="MaxRoundPeriod"/>。 
    /// 例如,在3代人违约和周期连续连续2-2分钟之间随机发射的情况下,a grain 完全彻底 
    /// 冷在过滤器中保留4.5分钟,然后(平均)扔掉。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 设置此值为 1 有效禁用保留。 过滤器将在每次 
    /// 重新分割回合,意思是 grain 必须在当前确切周期中连续活动才能保持固定状态。
    /// 这使得过滤器对当前流量反应非常积极,但容易在短暂闲置期间忘记固定 grains 。
    /// </para>
    /// </remarks>
    public int AnchoringFilterGenerations { get; set; } = DEFAULT_ANCHORING_FILTER_GENERATIONS;

    /// <summary>
    /// 默认值 <see cref="AnchoringFilterGenerations"/>。
    /// </summary>
    public const int DEFAULT_ANCHORING_FILTER_GENERATIONS = 3;
}

internal sealed class ActivationRepartitionerOptionsValidator(IOptions<ActivationRepartitionerOptions> options) : IConfigurationValidator
{
    private readonly ActivationRepartitionerOptions _options = options.Value;

    public void ValidateConfiguration()
    {
        if (_options.MaxEdgeCount <= 0)
        {
            ThrowMustBeGreaterThanZero(nameof(ActivationRepartitionerOptions.MaxEdgeCount));
        }

        if (_options.MaxUnprocessedEdges <= 0)
        {
            ThrowMustBeGreaterThanZero(nameof(ActivationRepartitionerOptions.MaxUnprocessedEdges));
        }

        if (_options.MinRoundPeriod < TimeSpan.Zero)
        {
            ThrowMustBeGreaterThanOrEqualToZero(nameof(ActivationRepartitionerOptions.MinRoundPeriod));
        }

        if (_options.MaxRoundPeriod <= TimeSpan.Zero)
        {
            ThrowMustBeGreaterThanZero(nameof(ActivationRepartitionerOptions.MaxRoundPeriod));
        }

        if (_options.RecoveryPeriod < TimeSpan.Zero)
        {
            ThrowMustBeGreaterThanOrEqualToZero(nameof(ActivationRepartitionerOptions.RecoveryPeriod));
        }

        if (_options.MaxRoundPeriod < _options.MinRoundPeriod)
        {
            ThrowMustBeGreaterThanOrEqualTo(nameof(ActivationRepartitionerOptions.MaxRoundPeriod), nameof(ActivationRepartitionerOptions.MinRoundPeriod));
        }

        if (_options.MinRoundPeriod < _options.RecoveryPeriod)
        {
            ThrowMustBeGreaterThanOrEqualTo(nameof(ActivationRepartitionerOptions.MinRoundPeriod), nameof(ActivationRepartitionerOptions.RecoveryPeriod));
        }

        if (_options.ProbabilisticFilteringMaxAllowedErrorRate < 0.001d || _options.ProbabilisticFilteringMaxAllowedErrorRate > 0.01d)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRepartitionerOptions.ProbabilisticFilteringMaxAllowedErrorRate)} must be inclusive between [0.001 - 0.01](0.1% - 1%)");
        }

        if (_options.AnchoringFilterGenerations <= 0)
        {
            ThrowMustBeGreaterThanZero(nameof(ActivationRepartitionerOptions.AnchoringFilterGenerations));
        }
    }

    private static void ThrowMustBeGreaterThanOrEqualToZero(string propertyName)
        => throw new OrleansConfigurationException($"{propertyName} must be greater than or equal to 0.");

    private static void ThrowMustBeGreaterThanZero(string propertyName)
        => throw new OrleansConfigurationException($"{propertyName} must be greater than 0.");

    private static void ThrowMustBeGreaterThanOrEqualTo(string name1, string name2)
        => throw new OrleansConfigurationException($"{name1} must be greater than or equal to {name2}.");
}
