namespace Orleans.Durable.Tasks;

/// <summary>
/// 描述对持久任务代码的修改,其版本在旧任务代码开始的处决同时共存。
/// 尚未确定: 它的名称, 以及运行代码所支持的版本 。
/// </summary>
/// <remarks>
/// <para>
/// 通常作为静态字段, 并阅读执行中的版本
/// <see cref="Get"/> 代码分支在变化的任何地方:
/// <c>static readonly DurableVersion CarrierApi = new("carrier-api", minSupported: 0, maxSupported: 1);</c>. 每一份检查
/// 然后说明相同的范围,所以添加版本或退出的部署会编辑声明。
/// </para>
/// <para>
/// <see cref="Get"/> 返回所宣布名称的 <see cref="DurableTask.GetVersion(string, int, int)"/> 返回
/// 和范围。
/// </para>
/// </remarks>
public sealed class DurableVersion
{
    /// <summary>初始化更改描述 <paramref name="name"/>.</summary>
    /// <param name="name">更改的名称, 唯一在任务检查中进行更改。 名称不包含 <c>$</c>。</param>
    /// <param name="minSupported">
    /// 代码支持的最古老版本。 版本 <c>0</c> 是更改第一个版本之前的代码 。
    /// </param>
    /// <param name="maxSupported">最新版本,第一次记录中首次被检查的处决。</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空或包含 <c>$</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minSupported"/> 是负,或 <paramref name="maxSupported"/> 小于
    /// <paramref name="minSupported"/>.
    /// </exception>
    public DurableVersion(string name, int minSupported, int maxSupported)
    {
        DurableTaskContext.ValidateName(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(minSupported);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSupported, minSupported);
        Name = name;
        MinSupported = minSupported;
        MaxSupported = maxSupported;
    }

    /// <summary>获取更改的名称。</summary>
    public string Name { get; }

    /// <summary>获得代码支持的最古老版本。</summary>
    public int MinSupported { get; }

    /// <summary>获得最新版本,即第一次记录中首次得到检查的处决。</summary>
    public int MaxSupported { get; }

    /// <summary>返回运行中的耐久任务之后的修改版本。</summary>
    /// <returns>从 <see cref="MinSupported"/> 到 <see cref="MaxSupported"/>的版本。</returns>
    /// <remarks>
    /// 执行中的第一个检查在一次执行中决定并记录版本,因为
    /// <see cref="DurableTask.GetVersion(string, int, int)"/> 描述。在耐久任务之外,检查返回
    /// <see cref="MaxSupported"/>.
    /// </remarks>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 执行失败后主机取消了尝试, 例如因为执行过程记录了一个超出支持范围之外的版本,
    /// 以拖住它;例外的内部例外是 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法从调用代码中记录版本, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    public int Get() => DurableTaskContext.Ambient is { } context
        ? context.DecideVersion(Name, MinSupported, MaxSupported)
        : MaxSupported;

    /// <summary>返回运行中的耐久任务是否遵循了这一修改的 <paramref name="version"/> 或更新的任务。</summary>
    /// <param name="version">引入调用者选择的行为的版本。</param>
    /// <returns><see langword="true"/> 何时 <see cref="Get"/> 返回 <paramref name="version"/> 或以上。</returns>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 执行失败后主机取消了尝试, 例如因为执行过程记录了一个超出支持范围之外的版本,
    /// 以拖住它;例外的内部例外是 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    public bool IsAtLeast(int version) => Get() >= version;

    /// <inheritdoc />
    public override string ToString() => $"{Name} [{MinSupported}..{MaxSupported}]";
}
