using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表一个同步事件,该时,当信号被发出时,在释放单个服务员后自动重置。
    /// 此类型支持同时的信号器, 但只有一个服务员 。
    /// </summary>
    internal sealed class SingleWaiterAutoResetEvent : IValueTaskSource
    {
        // 信号显示事件已经发出信号,尚未重置。
        private const uint SignaledFlag = 1;

        // 等待表明服务员在场,等待信号。
        private const uint WaitingFlag = 1 << 1;

        // ResetMask 用于清除两个身份标志。
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

            // 重置状态 。
            ResetStatus();
        }

        /// <summary>
        /// 信号服务员。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Signal()
        {
            if ((_status & SignaledFlag) == SignaledFlag)
            {
                // 事件已被信号化 。
                return;
            }

            // 设定信号旗
            var status = Interlocked.Or(ref _status, SignaledFlag);

            // 如果有服务员,而信号旗没被设定 现在就叫醒服务员
            if ((status & SignaledFlag) != SignaledFlag && (status & WaitingFlag) == WaitingFlag)
            {
                // 请注意,在本说法中,我们正在检查不稳定的 _ status 字段。
                // 保证信号条件属实:
                // 标志和等待的旗帜都设置好了
                Debug.Assert((_status & (SignaledFlag | WaitingFlag)) == (SignaledFlag | WaitingFlag));
                _waitSource.SetResult(true);
            }
        }

        /// <summary>
        /// 等待事件信号。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask WaitAsync()
        {
            // 说有服务生
            var status = Interlocked.Or(ref _status, WaitingFlag);

            // 如果已经有服务员, 则错误在于此类设计是用来与单个服务员一起使用的 。
            if ((status & WaitingFlag) == WaitingFlag)
            {
                ThrowConcurrentWaitersNotSupported();
            }

            // 如果事件已经发出信号,请立即叫醒服务员。
            if ((status & SignaledFlag) == SignaledFlag)
            {
                // 仅重置状态, 因为等待源未设置 。
                // 我们知道,等待源未被设置,因为等待源仅在
                // 信号 () 指出“ 等待” 旗已设, 但未设“ 信号” 旗 。
                ResetStatus();
                return default;
            }

            return new(this, _waitSource.Version);
        }

        /// <summary>
        /// 当服务员处理事件信号时呼叫。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetStatus()
        {
            // 正在处理此事件, 现在清除“ 信号” 旗帜 。
            // 服务员已经不等了,所以也清除了“等待”旗。
            var status = Interlocked.And(ref _status, ResetMask);

            // 如果“等待”和“信号”旗帜尚未设置,
            Debug.Assert((status & (WaitingFlag | SignaledFlag)) == (WaitingFlag | SignaledFlag));
        }

        private static void ThrowConcurrentWaitersNotSupported() => throw new InvalidOperationException("Concurrent waiters are not supported");
    }
}
