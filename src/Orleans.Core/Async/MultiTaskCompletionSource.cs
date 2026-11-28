using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans;

/// <summary>
/// <see cref="TaskCompletionSource{TResult}"/> 的替代方案，仅在收到指定数量的信号后才完成。
/// </summary>
internal sealed class MultiTaskCompletionSource
{
    private readonly TaskCompletionSource _tcs;
    private int _count;

    /// <summary>
    /// 初始化 <see cref="MultiTaskCompletionSource"/> 类的新实例。
    /// </summary>
    /// <param name="count">
    /// 此任务完成源完成前必须收到的信号数量。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 计数值小于或等于零。
    /// </exception>
    public MultiTaskCompletionSource(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _count = count;
    }

    /// <summary>
    /// 获取在收到足够数量的信号时完成的任务。
    /// </summary>
    public Task Task => _tcs.Task;

    /// <summary>
    /// 向此实例发出信号。
    /// </summary>
    /// <exception cref="InvalidOperationException">此方法的调用次数超过最初指定的 count 参数所允许的次数。</exception>
    public void SetOneResult()
    {
        var current = Interlocked.Decrement(ref _count);
        if (current < 0)
        {
            throw new InvalidOperationException(
                "SetOneResult was called more times than initially specified by the count argument.");
        }

        if (current == 0)
        {
            _tcs.SetResult();
        }
    }
}
