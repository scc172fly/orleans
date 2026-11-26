// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks.Sources;

namespace Orleans.Connections.Transport.Sockets;

// 缩微的 https://github.com/dotnet/runtime/blob/82ca681cbac89d813a3ce397e0c665e6c051ed67/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L798 版本,
// 1. 不支持除 PipeScheduler (不同步上下文,没有任务调度器) 以外的任何自定义列表
// 2. 使用标记不进行 ValueTask 验证
// 3. 不支持使用除Async/wait(不试图捕捉和恢复处决环境)以外的用途
// 4. 不使用取消的牌子
internal class SocketAwaitableEventArgs : SocketAsyncEventArgs, IValueTaskSource
{
    private static readonly Action<object?> ContinuationCompleted = _ => { };
    private Action<object?>? _continuation;

    public SocketAwaitableEventArgs()
        : base(unsafeSuppressExecutionContextFlow: true)
    {
    }

    public bool IsCompleted { get; private set; }

    public Exception? Error => CreateException(SocketError);

    [MemberNotNullWhen(true, nameof(Error))]
    public bool HasError => SocketError != SocketError.Success;

    protected override void OnCompleted(SocketAsyncEventArgs _)
    {
        IsCompleted = true;
        var continuation = _continuation;

        if (continuation != null || (continuation = Interlocked.CompareExchange(ref _continuation, ContinuationCompleted, null)) != null)
        {
            var state = UserToken;
            UserToken = null;
            _continuation = ContinuationCompleted; // 万一有人投票 IsCompleted

            // 执行续用内嵌 。
            continuation(state);
        }
    }

    public void GetResult(short token)
    {
        _continuation = null;
        IsCompleted = false;
        if (HasError) ThrowError();

        void ThrowError() => throw Error;
    }

    protected static SocketException? CreateException(SocketError e)
    {
        if (e is SocketError.Success) return null;
        return new SocketException((int)e);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return !ReferenceEquals(_continuation, ContinuationCompleted) ? ValueTaskSourceStatus.Pending :
                SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
                ValueTaskSourceStatus.Faulted;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        UserToken = state;
        var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
        if (ReferenceEquals(prevContinuation, ContinuationCompleted))
        {
            UserToken = null;

            // 执行续用内嵌 。
            continuation(state);
        }
    }
}
