using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示一种同步事件，在收到信号并释放单个等待者后会自动重置。
    /// 此类型支持并发发出信号的多个线程，但只允许一个等待者。
    /// </summary>
    internal sealed class SingleWaiterAutoResetEvent : IValueTaskSource
    {
        // Signaled 表示事件已收到信号但尚未重置。
        private const uint SignaledFlag = 1;

        // Waiting 表示存在等待者，正在等待事件收到信号。
        private const uint WaitingFlag = 1 << 1;

        // ResetMask 用于清除两个状态标志。
        private const uint ResetMask = ~SignaledFlag & ~WaitingFlag;

        private ManualResetValueTaskSourceCore<bool> _waitSource;
        private volatile uint _status;

        public bool RunContinuationsAsynchronously
        {
            get => _waitSource.RunContinuationsAsynchronously;
            set => _waitSource.RunContinuationsAsynchronously = value;
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _waitSource.GetStatus(token);

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _waitSource.OnCompleted(continuation, state, token, flags);

        void IValueTaskSource.GetResult(short token)
        {
            // 重置等待源。
            _waitSource.GetResult(token);
            _waitSource.Reset();

            // 重置状态。
            ResetStatus();
        }

        /// <summary>
        /// 向等待者发出信号。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Signal()
        {
            if ((_status & SignaledFlag) == SignaledFlag)
            {
                // 事件已经收到信号。
                return;
            }

            // 设置已收到信号的标志。
            var status = Interlocked.Or(ref _status, SignaledFlag);

            // 如果已有等待者且已收到信号的标志尚未设置，则立即唤醒等待者。
            if ((status & SignaledFlag) != SignaledFlag && (status & WaitingFlag) == WaitingFlag)
            {
                // 注意，此断言检查的是 volatile _status 字段。
                // 这是一个合理性检查，用于确保发出信号的条件成立：
                // Signaled 和 Waiting 两个标志均已设置。
                Debug.Assert((_status & (SignaledFlag | WaitingFlag)) == (SignaledFlag | WaitingFlag));
                _waitSource.SetResult(true);
            }
        }

        /// <summary>
        /// 等待事件收到信号。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask WaitAsync()
        {
            // 表示存在等待者。
            var status = Interlocked.Or(ref _status, WaitingFlag);

            // 如果已存在等待者，则属于错误，因为此类仅设计为供单个等待者使用。
            if ((status & WaitingFlag) == WaitingFlag)
            {
                ThrowConcurrentWaitersNotSupported();
            }

            // 如果事件已收到信号，则立即唤醒等待者。
            if ((status & SignaledFlag) == SignaledFlag)
            {
                // 只重置状态，因为尚未设置 _waitSource。
                // 可以确定 _waitSource 尚未设置，因为只有在
                // Signal() 观察到 Waiting 标志已设置而 Signaled 标志尚未设置时，才会设置 _waitSource。
                ResetStatus();
                return default;
            }

            return new(this, _waitSource.Version);
        }

        /// <summary>
        /// 在等待者处理事件信号时调用。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetStatus()
        {
            // 事件正在被处理，因此现在清除 Signaled 标志。
            // 等待者不再等待，因此也清除 Waiting 标志。
            var status = Interlocked.And(ref _status, ResetMask);

            // 如果 Waiting 和 Signaled 两个标志此前并未同时设置，则发生了严重错误。
            Debug.Assert((status & (WaitingFlag | SignaledFlag)) == (WaitingFlag | SignaledFlag));
        }

        private static void ThrowConcurrentWaitersNotSupported() => throw new InvalidOperationException("Concurrent waiters are not supported");
    }
}
