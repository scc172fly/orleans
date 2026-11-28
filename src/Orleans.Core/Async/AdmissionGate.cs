using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Internal;

/// <summary>
/// 在永久关闭准入之前允许并发操作进入，关闭后异步等待所有已获准的操作完成。
/// </summary>
/// <remarks>
/// 准入门的所有方法都支持并发调用。将每次 <see cref="TryEnter"/> 的结果保存在 <c>using</c>
/// 局部变量中，并在开始工作前检查 <see cref="Admission.Entered"/>。每个已获准的令牌只有一个
/// 所有者，由其恰好释放一次。转移所有权时，每次成功的
/// <see cref="TryEnterUnscoped"/> 都必须恰好对应一次 <see cref="Exit"/> 调用。
/// 此准入门最多支持 <see cref="int.MaxValue"/> 次尚未结束的进入尝试。
/// </remarks>
internal sealed class AdmissionGate
{
    private const int Closed = int.MinValue;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 符号位用于关闭准入，其余位记录进入尝试的数量，直至尝试被拒绝或完成。
    // 在同一个原子状态中更新两者，使每次准入操作与关闭操作具有确定的先后顺序。
    private int _state;

    /// <summary>
    /// 尝试允许一个操作进入，并与 <see cref="CloseAsync"/> 保持原子的先后顺序。
    /// </summary>
    /// <returns>
    /// 一个令牌，其 <see cref="Admission.Entered"/> 属性指示操作是否获准进入。
    /// 操作完成时释放该令牌，建议使用 <c>using</c> 声明。
    /// </returns>
    public Admission TryEnter() => TryEnterUnscoped() ? new(this) : default;

    /// <summary>
    /// 尝试允许一个操作进入，其所有者将在操作完成时显式调用 <see cref="Exit"/>。
    /// </summary>
    /// <returns>
    /// 获准进入时为 <see langword="true"/>，且与 <see cref="CloseAsync"/> 保持原子的先后顺序。
    /// 每次成功准入都必须恰好对应一次 <see cref="Exit"/>，包括发生异常或取消的路径。
    /// 被拒绝的尝试不持有准入资格。
    /// </returns>
    public bool TryEnterUnscoped()
    {
        // 避免关闭后新的被拒绝尝试延长等待全部操作退出的时间。
        if (Volatile.Read(ref _state) < 0)
        {
            return false;
        }

        if (Interlocked.Increment(ref _state) > 0)
        {
            return true;
        }

        // 被拒绝的尝试仍占用一个计数，且可能是最后一个释放计数的尝试。
        Exit();
        return false;
    }

    /// <summary>
    /// 释放一次成功准入。所有者在获准的操作完成后恰好调用此方法一次。
    /// </summary>
    public void Exit()
    {
        if (Interlocked.Decrement(ref _state) == Closed)
        {
            _drained.TrySetResult();
        }
    }

    /// <summary>
    /// 返回前永久关闭准入，并在全部获准操作退出后完成返回的任务。
    /// </summary>
    /// <returns>
    /// 共享的排空任务，后续调用也返回该任务。等待中的延续操作以异步方式运行。
    /// </returns>
    public Task CloseAsync()
    {
        if (Interlocked.Or(ref _state, Closed) == 0)
        {
            _drained.TrySetResult();
        }

        return _drained.Task;
    }

    /// <summary>
    /// 表示准入资格在作用域内的所有权。默认令牌表示进入被拒绝，可以安全释放。
    /// </summary>
    /// <remarks>
    /// 将令牌保存在单个 <c>using</c> 局部变量中。对已获准的令牌，无论有多少副本，都必须恰好释放一次。
    /// </remarks>
    public readonly struct Admission : IDisposable
    {
        private readonly AdmissionGate? _gate;

        internal Admission(AdmissionGate gate) => _gate = gate;

        /// <summary>
        /// 获取操作是否已获准进入。
        /// </summary>
        public bool Entered => _gate is not null;

        /// <summary>
        /// 在操作完成时释放此令牌持有的准入资格。
        /// </summary>
        public void Dispose() => _gate?.Exit();
    }
}
