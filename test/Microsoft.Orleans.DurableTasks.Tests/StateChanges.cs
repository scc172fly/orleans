#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Diagnostics;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 在可能使测试条件成立的状态转换时等待测试条件，而不是通过轮询等待。
/// </summary>
/// <remarks>
/// 每个 <see cref="DurableTaskEvents"/> 事件，以及测试替身通过 <see cref="Notify"/> 报告的每次更改，
/// 都会唤醒等待操作，使其重新判断条件是否成立。运行时在暂存
/// 状态转换时同步引发事件，因此等待操作被唤醒时即可根据运行时的新状态判断条件。超时的等待操作会指出其
/// 等待的条件。
/// </remarks>
internal static class StateChanges
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly IDisposable Subscription;
    private static TaskCompletionSource _next = Create();

    static StateChanges() => Subscription = DurableTaskEvents.AllEvents.Subscribe(new Observer());

    /// <summary>唤醒等待操作，使其重新判断各自的条件是否成立。</summary>
    public static void Notify() => Interlocked.Exchange(ref _next, Create()).TrySetResult();

    /// <summary>等待 <paramref name="condition"/> 成立。</summary>
    /// <exception cref="TimeoutException">条件未在超时时间内成立。</exception>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        GC.KeepAlive(Subscription);
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        expiry.CancelAfter(timeout ?? DefaultTimeout);
        while (true)
        {
            // 此次读取之后发生的更改会使捕获的信号完成，因此不会遗漏任何更改。
            var changed = Volatile.Read(ref _next).Task;
            if (condition())
            {
                return;
            }

            try
            {
                await changed.WaitAsync(expiry.Token);
            }
            catch (OperationCanceledException) when (!Xunit.TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The condition '{description}' didn't hold within {timeout ?? DefaultTimeout}.");
            }
        }
    }

    private static TaskCompletionSource Create() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Observer : IObserver<DurableTaskEvents.DurableTaskEvent>
    {
        public void OnNext(DurableTaskEvents.DurableTaskEvent value) => Notify();

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }
}
