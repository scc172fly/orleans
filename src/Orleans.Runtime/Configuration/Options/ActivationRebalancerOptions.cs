using System;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 用于配置激活实例再均衡的选项。
/// </summary>
public sealed class ActivationRebalancerOptions
{
    /// <summary>
    /// 再均衡器启动首次会话前的延迟时间。
    /// </summary>
    public TimeSpan RebalancerDueTime { get; set; } = DEFAULT_REBALANCER_DUE_TIME;

    /// <summary>
    /// <see cref="RebalancerDueTime"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_REBALANCER_DUE_TIME = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 一次会话中相邻两轮再均衡周期之间的时间间隔。
    /// </summary>
    /// <remarks>该值必须大于 2 倍的 <see cref="DeploymentLoadPublisherOptions.DeploymentLoadPublisherRefreshTime"/>。</remarks>
    public TimeSpan SessionCyclePeriod { get; set; } = DEFAULT_SESSION_CYCLE_PERIOD;

    /// <summary>
    /// <see cref="SessionCyclePeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_SESSION_CYCLE_PERIOD = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 集群熵没有显著改善的最大连续周期数。
    /// </summary>
    /// <remarks>此值包含边界，即若该值为 n，则第 n+1 个周期将停止当前再均衡会话。</remarks>
    public int MaxStagnantCycles { get; set; } = DEFAULT_MAX_STAGNANT_CYCLES;

    /// <summary>
    /// <see cref="MaxStagnantCycles"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MAX_STAGNANT_CYCLES = 3;

    /// <summary>
    /// 集群熵变化量中被视为改善的最小值。
    /// 如果连续经历 n 个停滞周期，其间熵的变化量始终小于
    /// 该阈值，则当前再均衡会话将停止。变化量为归一化值，
    /// 即相对于理论最大熵的值。
    /// </summary>
    /// <remarks>允许范围：(0-0.1]。</remarks>
    public double EntropyQuantum { get; set; } = DEFAULT_ENTROPY_QUANTUM;

    /// <summary>
    /// <see cref="EntropyQuantum"/> 的默认值。
    /// </summary>
    public const double DEFAULT_ENTROPY_QUANTUM = 0.0001d;

    /// <summary>
    /// 表示集群当前熵与理论最大熵之间允许的偏差。
    /// 低于该值的偏差在实践中视为已达到最大熵，当前再均衡会话将停止。
    /// 如果将 <see cref="ScaleAllowedEntropyDeviation"/> 设置为 <see langword="true"/>，则此值用作基础偏差率。
    /// </summary>
    /// <remarks>允许范围：(0-0.1]。</remarks>
    public double AllowedEntropyDeviation { get; set; } = DEFAULT_ALLOWED_ENTROPY_DEVIATION;

    /// <summary>
    /// <see cref="AllowedEntropyDeviation"/> 的默认值。
    /// </summary>
    public const double DEFAULT_ALLOWED_ENTROPY_DEVIATION = 0.0001d;

    /// <summary>
    /// 确定是否应根据激活实例总数动态调整 <see cref="AllowedEntropyDeviation"/>
    /// 的大小。设置为 <see langword="true"/> 时，允许的熵偏差
    /// 会在达到 <see cref="ScaledEntropyDeviationActivationThreshold"/> 后按对数增长，
    /// 并且上限为 <see cref="MAX_SCALED_ENTROPY_DEVIATION"/>。
    /// </summary>
    /// <remarks>之所以引入此机制，是因为例如 10 个激活实例的偏差对于总计
    /// 100,000 个激活实例的影响远小于对于总计 1,000 个激活实例的影响。</remarks>
    public bool ScaleAllowedEntropyDeviation { get; set; } = DEFAULT_SCALE_ALLOWED_ENTROPY_DEVIATION;

    /// <summary>
    /// <see cref="ScaleAllowedEntropyDeviation"/> 的默认值。
    /// </summary>
    public const bool DEFAULT_SCALE_ALLOWED_ENTROPY_DEVIATION = true;

    /// <summary>
    /// 当 <see cref="ScaleAllowedEntropyDeviation"/> 为 <see langword="true"/> 时允许的最大值。
    /// </summary>
    public const double MAX_SCALED_ENTROPY_DEVIATION = 0.1d;

    /// <summary>
    /// 确定任意再均衡周期中必须处于活动状态的激活实例数量，以便 <see cref="ScaleAllowedEntropyDeviation"/>
    /// （当且仅当其值为 <see langword="true"/> 时）开始调整 <see cref="AllowedEntropyDeviation"/>。
    /// </summary>
    /// <remarks>
    /// <para>允许范围：[1000-∞)。</para>
    /// <para><strong>不建议使用低于默认值的值。</strong></para>
    /// </remarks>
    public int ScaledEntropyDeviationActivationThreshold { get; set; } = DEFAULT_SCALED_ENTROPY_DEVIATION_ACTIVATION_THRESHOLD;

    /// <summary>
    /// <see cref="ScaledEntropyDeviationActivationThreshold"/> 的默认值。
    /// </summary>
    public const int DEFAULT_SCALED_ENTROPY_DEVIATION_ACTIVATION_THRESHOLD = 10_000;

    /// <summary>
    /// <para>表示一次再均衡会话期间已完成的再均衡周期数量所占的权重。</para>
    /// 修改此值对迁移速率的影响远大于修改 <see cref="SiloNumberWeight"/>，适合控制会话持续时间。
    /// <para>若希望提高迁移速率，请选择较高的值。</para>
    /// </summary>
    /// <remarks>允许范围：(0-1]。</remarks>
    public double CycleNumberWeight { get; set; } = DEFAULT_CYCLE_NUMBER_WEIGHT;

    /// <summary>
    /// <see cref="CycleNumberWeight"/> 的默认值。
    /// </summary>
    public const double DEFAULT_CYCLE_NUMBER_WEIGHT = 0.1d;

    /// <summary>
    /// <para>表示一次再均衡会话期间集群中 Silo 数量所占的权重。</para>
    /// 修改此值对迁移速率的影响远小于修改 <see cref="CycleNumberWeight"/>，适合微调。
    /// <para>若希望提高迁移速率，请选择较低的值。</para>
    /// </summary>
    /// <remarks>允许范围：[0-1]。</remarks>
    public double SiloNumberWeight { get; set; } = DEFAULT_SILO_NUMBER_WEIGHT;

    /// <summary>
    /// <see cref="SiloNumberWeight"/> 的默认值。
    /// </summary>
    public const double DEFAULT_SILO_NUMBER_WEIGHT = 0.1d;

    /// <summary>
    /// 任意一次周期允许迁移的最大激活实例数量。
    /// </summary>
    public int ActivationMigrationCountLimit { get; set; } = DEFAULT_ACTIVATION_MIGRATION_COUNT_LIMIT;

    /// <summary>
    /// <see cref="ActivationMigrationCountLimit"/> 的默认值。
    /// 默认值实际上没有限制。
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
