using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 管理设置设置的设置 grains 使用时横横集 <see cref="ResourceOptimizedPlacement"/>.
/// </summary>
/// <remarks><i>体重的所有属性, 彼此相相对。</i></remarks>
public sealed class ResourceOptimizedPlacementOptions
{
    /// <summary>
    /// silo使用CPU的重要性。
    /// </summary>
    /// <remarks><i>
    /// <para>A <u>高</u> 导致有利于职位定位的值 silos 与 <u>下</u> 使用 cpu 。</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int CpuUsageWeight { get; set; } = DEFAULT_CPU_USAGE_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="CpuUsageWeight"/>。
    /// </summary>
    public const int DEFAULT_CPU_USAGE_WEIGHT = 40;

    /// <summary>
    /// silo记忆用法的重要性。
    /// </summary>
    /// <remarks><i>
    /// <para>A <u>高</u> 导致有利于职位定位的值 silos 与 <u>下</u> 内存使用量。</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int MemoryUsageWeight { get; set; } = DEFAULT_MEMORY_USAGE_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="MemoryUsageWeight"/>。
    /// </summary>
    public const int DEFAULT_MEMORY_USAGE_WEIGHT = 20;

    /// <summary>
    /// 可用存储对 silo的重要性。
    /// </summary>
    /// <remarks><i>
    /// <para>A <u>高</u> 导致有利于职位设置的值 silos 与 <u>高</u> 可使用的内存 。</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int AvailableMemoryWeight { get; set; } = DEFAULT_AVAILABLE_MEMORY_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="AvailableMemoryWeight"/>。
    /// </summary>
    public const int DEFAULT_AVAILABLE_MEMORY_WEIGHT = 20;

    /// <summary>
    /// 最大可用内存对 silo的重要性。
    /// </summary>
    /// <remarks><i>
    /// <para>A <u>高</u> 导致有利于职位设置的值 silos 与 <u>高</u> 最大可用内存。</para>
    /// <para>这可能对资源分布不均的集群产生影响。 silos.</para>
    /// <para>这与 silo中的物理内存和配置的内存限制(如果已经配置)有强烈关联。</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int MaxAvailableMemoryWeight { get; set; } = DEFAULT_MAX_AVAILABLE_MEMORY_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="MaxAvailableMemoryWeight"/>。
    /// </summary>
    public const int DEFAULT_MAX_AVAILABLE_MEMORY_WEIGHT = 5;

    /// <summary>
    /// 当前激活计数对 silo的重要性。
    /// </summary>
    /// <remarks><i>
    /// <para>A <u>高</u> 导致有利于职位设置的值 silos 与 <u>下</u> 启动计数</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int ActivationCountWeight { get; set; } = DEFAULT_ACTIVATION_COUNT_WEIGHT;

    /// <summary>
    /// 默认值 <see cref="ActivationCountWeight"/>。
    /// </summary>
    public const int DEFAULT_ACTIVATION_COUNT_WEIGHT = 15;

    /// <summary>
    /// 具体差幅:如果两个 silos (其中一个是当前待激活的当地值),其利用率得分在本幅度内应视为“相同”。
    /// <list type="bullet">
    /// <item><description>当该值为 0 时, 该政策将始终有利于资源利用率较低的 silo , 即使 silo 距离当前待激活的时间很远。</description></item>
    /// <item><description>当该值为100时, 该政策将始终偏向于本地 silo, 不论其相对利用率分为何。 此政策基本上等同于 <see cref="PreferLocalPlacement"/>。</description></item>
    /// </list>
    /// </summary>
    /// <remarks><i>
    /// <para>请将此 e.g的低值减为: 5- 10</para>
    /// <para>有效范围是[0-100]</para>
    /// </i></remarks>
    public int LocalSiloPreferenceMargin { get; set; } = DEFAULT_LOCAL_SILO_PREFERENCE_MARGIN;

    /// <summary>
    /// 默认值 <see cref="LocalSiloPreferenceMargin"/>。
    /// </summary>
    public const int DEFAULT_LOCAL_SILO_PREFERENCE_MARGIN = 5;
}

internal sealed class ResourceOptimizedPlacementOptionsValidator
    (IOptions<ResourceOptimizedPlacementOptions> options) : IConfigurationValidator
{
    private readonly ResourceOptimizedPlacementOptions _options = options.Value;

    public void ValidateConfiguration()
    {
        if (_options.CpuUsageWeight < 0 || _options.CpuUsageWeight > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.CpuUsageWeight));
        }

        if (_options.MemoryUsageWeight < 0 || _options.MemoryUsageWeight > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.MemoryUsageWeight));
        }

        if (_options.AvailableMemoryWeight < 0 || _options.AvailableMemoryWeight > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.AvailableMemoryWeight));
        }

        if (_options.MaxAvailableMemoryWeight < 0 || _options.MaxAvailableMemoryWeight > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.MaxAvailableMemoryWeight));
        }

        if (_options.ActivationCountWeight < 0 || _options.ActivationCountWeight > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.ActivationCountWeight));
        }

        if (_options.LocalSiloPreferenceMargin < 0 || _options.LocalSiloPreferenceMargin > 100)
        {
            ThrowOutOfRange(nameof(ResourceOptimizedPlacementOptions.LocalSiloPreferenceMargin));
        }

        static void ThrowOutOfRange(string propertyName)
            => throw new OrleansConfigurationException($"{propertyName} must be inclusive between [0-100]");
    }
}
