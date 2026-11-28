// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks.Sources;

namespace Orleans.Connections.Transport.Sockets;

// 以下代码的精简版本：https://github.com/dotnet/runtime/blob/82ca681cbac89d813a3ce397e0c665e6c051ed67/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L798，其特点如下：
// 1. 不支持 PipeScheduler 以外的任何自定义调度（无同步上下文，无任务调度器）。
// 2. 不使用令牌执行 ValueTask 验证。
// 3. 不支持在 async/await 之外使用（不会尝试捕获和恢复执行上下文）。
// 4. 不使用取消令牌。
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
            _continuation = ContinuationCompleted; // 以防有人轮询 IsCompleted。

            // 内联执行延续操作。
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

            // 内联执行延续操作。
            continuation(state);
        }
    }
}
