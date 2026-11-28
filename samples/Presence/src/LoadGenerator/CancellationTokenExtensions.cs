namespace Presence.LoadGenerator;

/// <summary>
/// 取消令牌的辅助扩展方法。
/// </summary>
public static class CancellationTokenExtensions
{
    /// <summary>
    /// 获取一个任务，当此取消令牌被取消时，该任务完成。
    /// </summary>
    public static Task GetCompletionTask(this CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = token.Register(() => completion.TrySetResult(true));
        return completion.Task;
    }
}
