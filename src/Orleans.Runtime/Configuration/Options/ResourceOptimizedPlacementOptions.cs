using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration;

/// <summary>
/// 使用 <see cref="ResourceOptimizedPlacement"/> 时调节集群内 Grain 放置的设置。
/// </summary>
/// <remarks><i>所有权重属性的取值均是相对的。</i></remarks>
public sealed class ResourceOptimizedPlacementOptions
{
    /// <summary>
    /// Silo 的 CPU 使用量的重要程度。
    /// </summary>
    /// <remarks><i>
    /// <para><u>较高</u>的值会使放置更偏向 CPU 使用量<u>较低</u>的 Silo。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int CpuUsageWeight { get; set; } = DEFAULT_CPU_USAGE_WEIGHT;

    /// <summary>
    /// <see cref="CpuUsageWeight"/> 的默认值。
    /// </summary>
    public const int DEFAULT_CPU_USAGE_WEIGHT = 40;

    /// <summary>
    /// Silo 的内存使用量的重要程度。
    /// </summary>
    /// <remarks><i>
    /// <para><u>较高</u>的值会使放置更偏向内存使用量<u>较低</u>的 Silo。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int MemoryUsageWeight { get; set; } = DEFAULT_MEMORY_USAGE_WEIGHT;

    /// <summary>
    /// <see cref="MemoryUsageWeight"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MEMORY_USAGE_WEIGHT = 20;

    /// <summary>
    /// Silo 可用内存的重要程度。
    /// </summary>
    /// <remarks><i>
    /// <para><u>较高</u>的值会使放置更偏向可用内存<u>较多</u>的 Silo。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int AvailableMemoryWeight { get; set; } = DEFAULT_AVAILABLE_MEMORY_WEIGHT;

    /// <summary>
    /// <see cref="AvailableMemoryWeight"/> 的默认值。
    /// </summary>
    public const int DEFAULT_AVAILABLE_MEMORY_WEIGHT = 20;

    /// <summary>
    /// Silo 最大可用内存的重要程度。
    /// </summary>
    /// <remarks><i>
    /// <para><u>较高</u>的值会使放置更偏向最大可用内存<u>较多</u>的 Silo。</para>
    /// <para>当集群内各 Silo 的资源分布不均时，此项可能产生影响。</para>
    /// <para>这与 Silo 中的物理内存及配置的内存限制（如果已配置）密切相关。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int MaxAvailableMemoryWeight { get; set; } = DEFAULT_MAX_AVAILABLE_MEMORY_WEIGHT;

    /// <summary>
    /// <see cref="MaxAvailableMemoryWeight"/> 的默认值。
    /// </summary>
    public const int DEFAULT_MAX_AVAILABLE_MEMORY_WEIGHT = 5;

    /// <summary>
    /// Silo 当前激活实例数的重要程度。
    /// </summary>
    /// <remarks><i>
    /// <para><u>较高</u>的值会使放置更偏向激活实例数<u>较少</u>的 Silo。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int ActivationCountWeight { get; set; } = DEFAULT_ACTIVATION_COUNT_WEIGHT;

    /// <summary>
    /// <see cref="ActivationCountWeight"/> 的默认值。
    /// </summary>
    public const int DEFAULT_ACTIVATION_COUNT_WEIGHT = 15;

    /// <summary>
    /// 指定容差范围：若两个 Silo（其中一个是当前待放置激活实例所在的本地 Silo）的资源利用率评分差距在此范围内，则视为相同。
    /// <list type="bullet">
    /// <item><description>当此值为 0 时，策略始终优先选择资源利用率较低的 Silo，即使相对于当前待放置激活实例，该 Silo 位于远程。</description></item>
    /// <item><description>当此值为 100 时，策略始终优先选择本地 Silo，无论其相对资源利用率评分如何。此策略实际上等同于 <see cref="PreferLocalPlacement"/>。</description></item>
    /// </list>
    /// </summary>
    /// <remarks><i>
    /// <para>建议此值偏低，例如 5-10。</para>
    /// <para>有效范围为 [0-100]。</para>
    /// </i></remarks>
    public int LocalSiloPreferenceMargin { get; set; } = DEFAULT_LOCAL_SILO_PREFERENCE_MARGIN;

    /// <summary>
    /// <see cref="LocalSiloPreferenceMargin"/> 的默认值。
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
