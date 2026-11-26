using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Internal;

/// <summary>
/// 承认同时进行作业,直至永久关闭,然后不时地等待获准的作业完成。
/// </summary>
/// <remarks>
/// 所有门门方法支持同时调用的人。 <see cref="TryEnter"/> 导致 <c>using</c>
/// 本地端并在开始工作前检查 <see cref="Admission.Entered"/> 。每个注册的牌子有一个单
/// 所有人将它完全处置一次。 对于转让的所有权, 每一对成功者都配对
/// <see cref="TryEnterUnscoped"/> ,有一个完全的 <see cref="Exit"/> 呼叫。
/// 大门支撑到 <see cref="int.MaxValue"/> 未结条目尝试 。
/// </remarks>
internal sealed class AdmissionGate
{
    private const int Closed = int.MinValue;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 符号位关闭了输入; 剩下的位子数计数尝试, 直到被拒绝或完成 。
    // 更新的原子国家 都下令每个供认都不关闭
    private int _state;

    /// <summary>
    /// 企图接受一项行动,从原子上下令对 <see cref="CloseAsync"/>进行。
    /// </summary>
    /// <returns>
    /// 其 <see cref="Admission.Entered"/> 财产表明是否允许手术的象征。
    /// 当操作完成时丢弃该标记, 最好使用 <c>using</c> 声明。
    /// </returns>
    public Admission TryEnter() => TryEnterUnscoped() ? new(this) : default;

    /// <summary>
    /// 试图接受一项操作,其所有人在完成后将明确呼叫 <see cref="Exit"/> 。
    /// </summary>
    /// <returns>
    /// <see langword="true"/> 入院时,按原子弹命令对 <see cref="CloseAsync"/>进行反对。
    /// 每一个成功录取都必须配有一 <see cref="Exit"/>,包括例外和取消路径。
    /// 拒绝的企图是不承认的。
    /// </returns>
    public bool TryEnterUnscoped()
    {
        // 保持新的被拒绝的尝试,避免关闭后延长排水管。
        if (Volatile.Read(ref _state) < 0)
        {
            return false;
        }

        if (Interlocked.Increment(ref _state) > 0)
        {
            return true;
        }

        // 被拒绝的企图仍然拥有一个伯爵,可能是最后释放它的人。
        Exit();
        return false;
    }

    /// <summary>
    /// 成功释放一个入场证明,它的主人在被承认的手术完成后 将这称为一次
    /// </summary>
    public void Exit()
    {
        if (Interlocked.Decrement(ref _state) == Closed)
        {
            _drained.TrySetResult();
        }
    }

    /// <summary>
    /// 返回前永久关闭入境,一旦所有获准的行动都撤离后,完成归还的任务。
    /// </summary>
    /// <returns>
    /// 共同排水任务,也因随后的电话而返回,等待的继续工作毫无节制。
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
    /// 表示一个进入的扩展所有权。默认标记代表拒绝的条目,可以安全处置。
    /// </summary>
    /// <remarks>
    /// 将牌标保留在单 <c>using</c> 本地端。在所有副本中完全一次性卸下一个注册的标记。
    /// </remarks>
    public readonly struct Admission : IDisposable
    {
        private readonly AdmissionGate? _gate;

        internal Admission(AdmissionGate gate) => _gate = gate;

        /// <summary>
        /// 查一下手术是否被接受
        /// </summary>
        public bool Entered => _gate is not null;

        /// <summary>
        /// 当该代号的操作完成后, 将释放此代号的承认 。
        /// </summary>
        public void Dispose() => _gate?.Exit();
    }
}
