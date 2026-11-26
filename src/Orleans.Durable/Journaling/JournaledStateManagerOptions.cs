using Orleans.Durable.Journaling.Json;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 <see cref="IJournaledStateManager"/>的选项。
/// </summary>
public sealed class JournaledStateManagerOptions
{
    /// <summary>
    /// 获取或设置用于写入日记数据的日记格式密钥。
    /// </summary>
    public string JournalFormatKey { get; set; } = JsonLinesJournalFormat.JournalFormatKey;

    /// <summary>
    /// 指定要等到管理器退休的时间间隔
    /// a <see cref="IStateMachine"/> ,如果它不再在经理注册。
    /// </summary>
    /// <remarks>
    /// <para>退休行为将这个国家从日记中删除。</para>
    /// <para>如果重新启用国家(在宽限期内),则管理人员不会删除国家。</para>
    /// <para>
    /// 此值表示 <b>最低</b> 国家的命运将推迟
    /// 最后决定需要较长的时间 -- -- 通常是 <see cref="RetirementGracePeriod"/> +[时间直到下一次压缩发生时]。
    /// </para>
    /// </remarks>
    public TimeSpan RetirementGracePeriod { get; set; } = DEFAULT_RETIREMENT_GRACE_PERIOD;

    /// <summary>
    /// 默认值 <see cref="RetirementGracePeriod"/>。
    /// </summary>
    public static readonly TimeSpan DEFAULT_RETIREMENT_GRACE_PERIOD = TimeSpan.FromDays(7);
}
