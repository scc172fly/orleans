using System;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 激活实例重新分区的配置选项。
/// </summary>
public sealed class ActivationRepartitionerOptions
{
    /// <summary>
    /// <para>
    /// 一轮重新分区期间在内存中保留的最大边数。每条边表示一个 Grain 调用另一个 Grain 的次数。
    /// </para>
    /// <para>
    /// 如果此值为 N，并不意味着一轮重新分区后会迁移 N 个激活实例。
    /// 也不意味着本轮排名很高的激活实例在下一轮仍会排名很高。
    /// 在最极端的情况下，迁移的激活实例数等于此值，因此可据此设置合理的数值。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 为节省内存，权重最大的连接采用概率方式记录，因此存在固有误差。
    /// 该误差与此值成反比，因此不建议使用小于 100 的值。如果系统收敛速度不够快，可考虑增大此值。
    /// </remarks>
    public int MaxEdgeCount { get; set; } = DEFAULT_MAX_EDGE_COUNT;

    /// <summary>
    /// <see cref="MaxEdgeCount"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MAX_EDGE_COUNT = 10_000;

    /// <summary>
    /// 发起两轮重新分区之间的最短时间间隔。
    /// </summary>
    /// <remarks>实际触发时间在此值与 <see cref="MaxRoundPeriod"/> 之间随机选取。</remarks>
    public TimeSpan MinRoundPeriod { get; set; } = DEFAULT_MINUMUM_ROUND_PERIOD;

    /// <summary>
    /// <see cref="MinRoundPeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MINUMUM_ROUND_PERIOD = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 发起两轮重新分区之间的最长时间间隔。
    /// </summary>
    /// <remarks>
    /// <para>实际触发时间在此值与 <see cref="MinRoundPeriod"/> 之间随机选取。</para>
    /// <para>为获得最佳效果，建议额外预留 10 秒乘以集群预计的最大 Silo 数量。</para>
    /// </remarks>
    public TimeSpan MaxRoundPeriod { get; set; } = DEFAULT_MAXIMUM_ROUND_PERIOD;

    /// <summary>
    /// <see cref="MaxRoundPeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_MAXIMUM_ROUND_PERIOD = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Silo 从上一轮重新分区恢复所需的最短时间。
    /// 在此时间结束之前，该 Silo 不会参与其他 Silo 发起的重新分区尝试。
    /// </summary>
    public TimeSpan RecoveryPeriod { get; set; } = DEFAULT_RECOVERY_PERIOD;

    /// <summary>
    /// <see cref="RecoveryPeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RECOVERY_PERIOD = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 缓冲区中尚未处理的边的最大数量。超过此值时，将丢弃最早的边。
    /// </summary>
    public int MaxUnprocessedEdges { get; set; } = DEFAULT_MAX_UNPROCESSED_EDGES;

    /// <summary>
    /// <see cref="MaxUnprocessedEdges"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MAX_UNPROCESSED_EDGES = 100_000;

    /// <summary>
    /// 获取或设置是否启用本地顶点过滤器的值。该过滤器跟踪
    /// 已合理分区的顶点（将它们移出本地主机会产生负面影响），并将它们合并
    /// 为每个 Silo 对应的单个顶点，以减少跟踪涉及该顶点的边所需的空间。这样会
    /// 降低精度，但可能显著提高重新分区器的有效性，因为
    /// 已合理分区的边不会占据 Top-K 数据结构的主要空间，从而留出足够空间跟踪
    /// 尚未合理分区的顶点。默认启用此功能。
    /// </summary>
    public bool AnchoringFilterEnabled { get; set; } = DEFAULT_ANCHORING_FILTER_ENABLED;

    /// <summary>
    /// <see cref="AnchoringFilterEnabled"/> 的默认值。
    /// </summary>
    public const bool DEFAULT_ANCHORING_FILTER_ENABLED = true;

    /// <summary>
    /// 当 <see cref="AnchoringFilterEnabled"/> 设置为 <see langword="true"/> 时允许的最大误差率，否则此设置不适用。
    /// </summary>
    /// <remarks>允许的范围：[0.001 - 0.01]（0.1% - 1%）。</remarks>
    public double ProbabilisticFilteringMaxAllowedErrorRate { get; set; } = DEFAULT_PROBABILISTIC_FILTERING_MAX_ALLOWED_ERROR;

    /// <summary>
    /// <see cref="ProbabilisticFilteringMaxAllowedErrorRate"/> 的默认值。
    /// </summary>
    public const double DEFAULT_PROBABILISTIC_FILTERING_MAX_ALLOWED_ERROR = 0.01d;

    /// <summary>
    /// <para>
    /// 决定锚定历史的保留时间。较少的代数能保持过滤器数据更新，但可能过快地遗忘已锚定的 Grain。
    /// 较多的代数可延长历史保留时间，但会增加内存占用和饱和风险。
    /// </para>
    /// <para>
    /// 由于过滤器在每轮重新分区时恰好衰减一次，因此“冷”Grain 保持锚定的实际时间直接取决于
    /// <see cref="MinRoundPeriod"/> 和 <see cref="MaxRoundPeriod"/>。
    /// 例如，默认保留 3 代且每轮随机间隔为 1 至 2 分钟时，一个完全
    /// “冷却”的 Grain 在被移除前将在过滤器中平均保留 4.5 分钟。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 将此值设为 1 实际上会禁用历史保留。过滤器将在每轮
    /// 重新分区开始时完全清空，这意味着 Grain 必须在当前轮次持续活跃才能保持锚定。
    /// 这样过滤器能快速响应当前流量，但容易在短暂空闲期间遗忘已锚定的 Grain。
    /// </para>
    /// </remarks>
    public int AnchoringFilterGenerations { get; set; } = DEFAULT_ANCHORING_FILTER_GENERATIONS;

    /// <summary>
    /// <see cref="AnchoringFilterGenerations"/> 的默认值。
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
