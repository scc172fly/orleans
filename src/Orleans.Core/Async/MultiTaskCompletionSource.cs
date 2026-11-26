using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans;

/// <summary>
/// 替代 <see cref="TaskCompletionSource{TResult}"/> 的替代方法,即只在收到特定数量信号后完成。
/// </summary>
internal sealed class MultiTaskCompletionSource
{
    private readonly TaskCompletionSource _tcs;
    private int _count;

    /// <summary>
    /// 初始化 <see cref="MultiTaskCompletionSource"/> 类的新实例。
    /// </summary>
    /// <param name="count">
    /// 完成源完成之前必须出现的信号数量。
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
    /// 获得收到足够数量信号后完成的任务。
    /// </summary>
    public Task Task => _tcs.Task;

    /// <summary>
    /// 发出这个信号
    /// </summary>
    /// <exception cref="InvalidOperationException">此方法被调用的次数超过最初指定的计数参数允许的倍数 。</exception>
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
