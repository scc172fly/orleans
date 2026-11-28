namespace Orleans.Durable.Tasks;

/// <summary>
/// 描述持久化任务代码的变更；在旧版代码上启动的执行实例仍处于
/// 待处理状态期间，各版本并存；描述包括变更名称以及当前运行代码支持的版本。
/// </summary>
/// <remarks>
/// <para>
/// 每项变更只声明一次，通常使用静态字段；当代码根据变更进行分支时，使用
/// <see cref="Get"/> 读取当前执行实例的版本：
/// <c>static readonly DurableVersion CarrierApi = new("carrier-api", minSupported: 0, maxSupported: 1);</c>。这样每次检查
/// 都指定相同的范围，因此当部署新增或淘汰版本时，只需修改该声明。
/// </para>
/// <para>
/// <see cref="Get"/> 的返回结果等同于针对声明的名称与范围调用 <see cref="DurableTask.GetVersion(string, int, int)"/>
/// 所得到的结果。
/// </para>
/// </remarks>
public sealed class DurableVersion
{
    /// <summary>初始化变更 <paramref name="name"/> 的描述。</summary>
    /// <param name="name">变更名称，在同一任务检查的各项变更中必须唯一。名称不能包含 <c>$</c>。</param>
    /// <param name="minSupported">
    /// 代码支持的最早版本。版本 <c>0</c> 表示首次引入该变更之前的代码。
    /// </param>
    /// <param name="maxSupported">最新版本；首次执行此版本检查的执行实例会记录该版本。</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或包含 <c>$</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minSupported"/> 为负值，或者 <paramref name="maxSupported"/> 小于
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

    /// <summary>获取变更名称。</summary>
    public string Name { get; }

    /// <summary>获取代码支持的最早版本。</summary>
    public int MinSupported { get; }

    /// <summary>获取最新版本；首次执行此版本检查的执行实例会记录该版本。</summary>
    public int MaxSupported { get; }

    /// <summary>返回正在运行的持久化任务所遵循的此变更的版本。</summary>
    /// <returns>范围从 <see cref="MinSupported"/> 到 <see cref="MaxSupported"/> 的版本。</returns>
    /// <remarks>
    /// 执行实例中的首次检查决定并记录版本，如
    /// <see cref="DurableTask.GetVersion(string, int, int)"/> 所述。在持久化任务之外，该检查返回
    /// <see cref="MaxSupported"/>.
    /// </remarks>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如因为执行实例记录了支持范围之外的版本，
    /// 导致其停滞；此时异常的内部异常为 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法从调用代码所在位置记录版本，例如在 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    public int Get() => DurableTaskContext.Ambient is { } context
        ? context.DecideVersion(Name, MinSupported, MaxSupported)
        : MaxSupported;

    /// <summary>返回正在运行的持久化任务是否遵循此变更的 <paramref name="version"/> 版本或更新版本。</summary>
    /// <param name="version">引入调用方所选行为的版本。</param>
    /// <returns><see langword="true"/> 表示 <see cref="Get"/> 返回的版本不小于 <paramref name="version"/>。</returns>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如因为执行实例记录了支持范围之外的版本，
    /// 导致其停滞；此时异常的内部异常为 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    public bool IsAtLeast(int version) => Get() >= version;

    /// <inheritdoc />
    public override string ToString() => $"{Name} [{MinSupported}..{MaxSupported}]";
}
