namespace Orleans.Durable.Tasks;

/// <summary>
/// 代表一个持久的任务, 其记录的修改版本不是运行代码支持的, 因为
/// 代码提高了该修改的最小支持版本, 或者因为旧代码重放执行中的新代码
/// 已记录。
/// </summary>
/// <remarks>
/// <see cref="DurableTask.GetVersion(string, int, int)"/> 中止执行:主机取消尝试
/// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/>,以及企图
/// <see cref="DurableTaskAttemptCanceledException"/> 将这一例外作为内部的例外。东道主抛弃了
/// 后启动,通常在使用代码支持
/// 记录版本,重放任务。中止任务以结束它。
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskVersionException")]
public sealed class DurableTaskVersionException : Exception
{
    /// <summary>初始化已录版本的例外 <paramref name="version"/> 联 年 月 日 月 日 月 月 日 <paramref name="name"/>.</summary>
    /// <param name="taskId">记录版本的执行。</param>
    /// <param name="name">更改的名称。</param>
    /// <param name="version">执行记录版本。</param>
    /// <param name="minSupported">运行代码所支持的最小版本。</param>
    /// <param name="maxSupported">运行代码所支持的最大版本。</param>
    public DurableTaskVersionException(TaskId taskId, string name, int version, int minSupported, int maxSupported)
        : base($"Durable task '{taskId}' recorded version {version} of '{name}', and the running code supports versions {minSupported} to {maxSupported}. The task stays pending until code which supports version {version} replays it.")
    {
        TaskId = taskId;
        Name = name;
        Version = version;
        MinSupported = minSupported;
        MaxSupported = maxSupported;
    }

    /// <summary>获取记录版本的执行。</summary>
    public TaskId TaskId { get; }

    /// <summary>获取更改的名称。</summary>
    public string Name { get; }

    /// <summary>获取执行的录音版本。</summary>
    public int Version { get; }

    /// <summary>获取运行代码所支持的最小版本。</summary>
    public int MinSupported { get; }

    /// <summary>获取运行代码所支持的最大版本。</summary>
    public int MaxSupported { get; }
}
