using System;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 配置激活再平衡的选项。
/// </summary>
public sealed class ActivationRebalancerOptions
{
    /// <summary>
    /// 重新平衡者开始首届会议的时机到了
    /// </summary>
    public TimeSpan RebalancerDueTime { get; set; } = DEFAULT_REBALANCER_DUE_TIME;

    /// <summary>
    /// 默认值 <see cref="RebalancerDueTime"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_REBALANCER_DUE_TIME = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 届会连续两次重新平衡周期之间的时间。
    /// </summary>
    /// <remarks>大于2 x <see cref="DeploymentLoadPublisherOptions.DeploymentLoadPublisherRefreshTime"/>。</remarks>
    public TimeSpan SessionCyclePeriod { get; set; } = DEFAULT_SESSION_CYCLE_PERIOD;

    /// <summary>
    /// 默认值 <see cref="SessionCyclePeriod"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_SESSION_CYCLE_PERIOD = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 周期的最大、连续次数,对集群的环流没有显著改善。
    /// </summary>
    /// <remarks>这个值是包容性的, i.e。如果这个值是“ n” ,那么“n+1” 周期将停止当前再平衡会话 。</remarks>
    public int MaxStagnantCycles { get; set; } = DEFAULT_MAX_STAGNANT_CYCLES;

    /// <summary>
    /// 默认值 <see cref="MaxStagnantCycles"/>。
    /// </summary>
    public const int DEFAULT_MAX_STAGNANT_CYCLES = 3;

    /// <summary>
    /// 被认为改进的组群的变异最小值。
    /// 当共N-连续停滞周期过后,在这段时间内, 的倍数变化小于
    /// 量值, 然后当前再平衡会停止。 改变是一个正常值
    /// 相对于最大可能的宇宙信通。
    /// </summary>
    /// <remarks>允许范围: (0-0.1)</remarks>
    public double EntropyQuantum { get; set; } = DEFAULT_ENTROPY_QUANTUM;

    /// <summary>
    /// 默认值 <see cref="EntropyQuantum"/>。
    /// </summary>
    public const double DEFAULT_ENTROPY_QUANTUM = 0.0001d;

    /// <summary>
    /// 代表该组当前星体的星体与理论最大值之间的允许星体偏差。
    /// 低于这个数值的值实际上被视为“最大”,而目前的再平衡会议将停止。
    /// 如果 <see cref="ScaleAllowedEntropyDeviation"/> 被设为 <see langword="true"/>,则此数值作为基准率。
    /// </summary>
    /// <remarks>允许的范围是: (0-0.1)</remarks>
    public double AllowedEntropyDeviation { get; set; } = DEFAULT_ALLOWED_ENTROPY_DEVIATION;

    /// <summary>
    /// 默认值 <see cref="AllowedEntropyDeviation"/>。
    /// </summary>
    public const double DEFAULT_ALLOWED_ENTROPY_DEVIATION = 0.0001d;

    /// <summary>
    /// 确定是否应该动态缩放 <see cref="AllowedEntropyDeviation"/>
    /// 根据激活的总数确定。设定到 <see langword="true"/>时,允许的 entropy
    /// 到达 <see cref="ScaledEntropyDeviationActivationThreshold"/>后, 偏差将对数增加,
    /// 并在 <see cref="MAX_SCALED_ENTROPY_DEVIATION"/>时设定上限。
    /// </summary>
    /// <remarks>之所以存在,是因为10的引爆率偏离了10,
    /// 共对100,000个启动发生影响,比1,000个启动发生作用时的影响要大。</remarks>
    public bool ScaleAllowedEntropyDeviation { get; set; } = DEFAULT_SCALE_ALLOWED_ENTROPY_DEVIATION;

    /// <summary>
    /// 默认值 <see cref="ScaleAllowedEntropyDeviation"/>。
    /// </summary>
    public const bool DEFAULT_SCALE_ALLOWED_ENTROPY_DEVIATION = true;

    /// <summary>
    /// <see cref="ScaleAllowedEntropyDeviation"/> 是 <see langword="true"/>时允许的最大值。
    /// </summary>
    public const double MAX_SCALED_ENTROPY_DEVIATION = 0.1d;

    /// <summary>
    /// 确定在任何再平衡周期内必须活跃的激活次数,以便 <see cref="ScaleAllowedEntropyDeviation"/>
    /// (如果而且只有在 <see langword="true"/>开始缩放 <see cref="AllowedEntropyDeviation"/>时)
    /// </summary>
    /// <remarks>
    /// <para>Allowed range: [1000-∞)</para>
    /// <para><strong>低于默认值的数值被劝阻。</strong></para>
    /// </remarks>
    public int ScaledEntropyDeviationActivationThreshold { get; set; } = DEFAULT_SCALED_ENTROPY_DEVIATION_ACTIVATION_THRESHOLD;

    /// <summary>
    /// 默认值 <see cref="ScaledEntropyDeviationActivationThreshold"/>。
    /// </summary>
    public const int DEFAULT_SCALED_ENTROPY_DEVIATION_ACTIVATION_THRESHOLD = 10_000;

    /// <summary>
    /// <para>表示对重新平衡会议期间所通过再平衡周期数目的权重。</para>
    /// 改变这一数值对移徙率的影响远大于 <see cref="SiloNumberWeight"/>,适合控制会期。
    /// <para>选择更高的值,如果你想加快迁移速度的话。</para>
    /// </summary>
    /// <remarks>允许范围: (0-1)</remarks>
    public double CycleNumberWeight { get; set; } = DEFAULT_CYCLE_NUMBER_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="CycleNumberWeight"/>。
    /// </summary>
    public const double DEFAULT_CYCLE_NUMBER_WEIGHT = 0.1d;

    /// <summary>
    /// <para>表示对数字的加权数 silos 重新平衡会议期间的一组。</para>
    /// 改变这一数值对移徙率的影响远远小于 <see cref="CycleNumberWeight"/>,适合微调。
    /// <para>选择较低数值,如果你想加快迁移速度的话。</para>
    /// </summary>
    /// <remarks>允许范围: [0-1]</remarks>
    public double SiloNumberWeight { get; set; } = DEFAULT_SILO_NUMBER_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="SiloNumberWeight"/>。
    /// </summary>
    public const double DEFAULT_SILO_NUMBER_WEIGHT = 0.1d;

    /// <summary>
    /// 在任何特定周期中可迁移的最大允许启动次数。
    /// </summary>
    public int ActivationMigrationCountLimit { get; set; } = DEFAULT_ACTIVATION_MIGRATION_COUNT_LIMIT;

    /// <summary>
    /// <see cref="ActivationMigrationCountLimit"/>的默认值。
    /// 默认实际上没有限制 。
    /// </summary>
    public const int DEFAULT_ACTIVATION_MIGRATION_COUNT_LIMIT = int.MaxValue;
}

internal sealed class ActivationRebalancerOptionsValidator(
    IOptions<ActivationRebalancerOptions> options,
    IOptions<DeploymentLoadPublisherOptions> publisherOptions) : IConfigurationValidator
{
    private readonly ActivationRebalancerOptions _options = options.Value;
    private readonly DeploymentLoadPublisherOptions _publisherOptions = publisherOptions.Value;

    public void ValidateConfiguration()
    {
        if (_options.SessionCyclePeriod < 2 * _publisherOptions.DeploymentLoadPublisherRefreshTime)
        {
            throw new OrleansConfigurationException(
                $"{nameof(ActivationRebalancerOptions.SessionCyclePeriod)} must be at least " +
                $"{$"2 x {nameof(DeploymentLoadPublisherOptions.DeploymentLoadPublisherRefreshTime)}"}");
        }

        if (_options.MaxStagnantCycles <= 0)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.MaxStagnantCycles)} must be greater than 0");
        }

        if (_options.EntropyQuantum <= 0d || _options.EntropyQuantum > 0.1d)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.EntropyQuantum)} must be in greater than 0, and less or equal 0.1");
        }

        if (_options.AllowedEntropyDeviation <= 0d || _options.AllowedEntropyDeviation > 0.1d)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.AllowedEntropyDeviation)} must be in greater than 0, and less or equal 0.1");
        }

        if (_options.CycleNumberWeight <= 0d || _options.CycleNumberWeight > 1d)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.CycleNumberWeight)} must be in greater than 0, and less or equal to 1");
        }

        if (_options.SiloNumberWeight < 0d || _options.SiloNumberWeight > 1d)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.SiloNumberWeight)} must be in greater than or equal to 0, and less or equal to 1");
        }

        if (_options.ActivationMigrationCountLimit < 1)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.ActivationMigrationCountLimit)} must be greater than 0");
        }

        if (_options.ScaledEntropyDeviationActivationThreshold < 1_000)
        {
            throw new OrleansConfigurationException($"{nameof(ActivationRebalancerOptions.ScaledEntropyDeviationActivationThreshold)} must be greater than or equal to 1000");
        }
    }
}
