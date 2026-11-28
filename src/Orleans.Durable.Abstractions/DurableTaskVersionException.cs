namespace Orleans.Durable.Tasks;

/// <summary>
/// 表示持久化任务已记录的变更版本不受当前运行代码支持的情况，原因可能是
/// 代码提高了该变更的最低支持版本，或者旧代码正在重放由新代码
/// 记录的执行过程。
/// </summary>
/// <remarks>
/// <see cref="DurableTask.GetVersion(string, int, int)"/> 使这种执行实例停滞：宿主以以下原因取消执行尝试：
/// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/>；此次尝试的
/// <see cref="DurableTaskAttemptCanceledException"/> 将此异常作为内部异常。宿主丢弃
/// 执行尝试的结果，并使任务保持待处理状态。之后的激活实例（通常位于代码支持
/// 已记录版本的部署中）会重放任务。若要直接结束任务，请中止它。
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskVersionException")]
public sealed class DurableTaskVersionException : Exception
{
    /// <summary>为已记录的版本 <paramref name="version"/>（对应变更 <paramref name="name"/>）初始化异常。</summary>
    /// <param name="taskId">记录版本的执行实例。</param>
    /// <param name="name">变更名称。</param>
    /// <param name="version">执行实例已记录的版本。</param>
    /// <param name="minSupported">当前运行代码支持的最低版本。</param>
    /// <param name="maxSupported">当前运行代码支持的最高版本。</param>
    public DurableTaskVersionException(TaskId taskId, string name, int version, int minSupported, int maxSupported)
        : base($"Durable task '{taskId}' recorded version {version} of '{name}', and the running code supports versions {minSupported} to {maxSupported}. The task stays pending until code which supports version {version} replays it.")
    {
        TaskId = taskId;
        Name = name;
        Version = version;
        MinSupported = minSupported;
        MaxSupported = maxSupported;
    }

    /// <summary>获取记录版本的执行实例。</summary>
    public TaskId TaskId { get; }

    /// <summary>获取变更名称。</summary>
    public string Name { get; }

    /// <summary>获取执行实例已记录的版本。</summary>
    public int Version { get; }

    /// <summary>获取当前运行代码支持的最低版本。</summary>
    public int MinSupported { get; }

    /// <summary>获取当前运行代码支持的最高版本。</summary>
    public int MaxSupported { get; }
}
