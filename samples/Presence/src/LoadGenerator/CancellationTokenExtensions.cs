namespace Presence.LoadGenerator;

/// <summary>
/// 取消代号的扩展辅助器 。
/// </summary>
public static class CancellationTokenExtensions
{
    /// <summary>
    /// 获取一个任务, 当取消此取消的代号时完成此项任务 。
    /// </summary>
    public static Task GetCompletionTask(this CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = token.Register(() => completion.TrySetResult(true));
        return completion.Task;
    }
}
