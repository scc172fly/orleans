using Orleans.Durable.Journaling.Json;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 <see cref="IJournaledStateManager"/> 的选项。
/// </summary>
public sealed class JournaledStateManagerOptions
{
    /// <summary>
    /// 获取或设置用于写入日志数据的日志格式键。
    /// </summary>
    public string JournalFormatKey { get; set; } = JsonLinesJournalFormat.JournalFormatKey;

    /// <summary>
    /// 指定管理器在以下情况下将状态退役之前的等待时间：
    /// <see cref="IStateMachine"/> 不再向管理器注册。
    /// </summary>
    /// <remarks>
    /// <para>退役操作将该状态从日志中移除。</para>
    /// <para>如果在宽限期内重新引入该状态，则管理器不会移除它。</para>
    /// <para>
    /// 此值表示延迟决定该状态去留的 <b>最短</b> 时间。
    /// 最终决定可能更晚，通常为 <see cref="RetirementGracePeriod"/> 加上等待下一次压实的时间。
    /// </para>
    /// </remarks>
    public TimeSpan RetirementGracePeriod { get; set; } = DEFAULT_RETIREMENT_GRACE_PERIOD;

    /// <summary>
    /// <see cref="RetirementGracePeriod"/> 的默认值。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RETIREMENT_GRACE_PERIOD = TimeSpan.FromDays(7);
}
