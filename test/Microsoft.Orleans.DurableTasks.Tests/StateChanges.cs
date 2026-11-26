#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Diagnostics;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 等待在过渡阶段进行测试,以使他们满意,而不是投票。
/// </summary>
/// <remarks>
/// 每一 <see cref="DurableTaskEvents"/> 和每个变化,测试假报告 <see cref="Notify"/>,
/// 时间流逝时会同步地增加事件
/// 等待时间的倒数 。 等待时间的间隔时间
/// 条件。
/// </remarks>
internal static class StateChanges
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly IDisposable Subscription;
    private static TaskCompletionSource _next = Create();

    static StateChanges() => Subscription = DurableTaskEvents.AllEvents.Subscribe(new Observer());

    /// <summary>唤醒了等待,所以他们再次评估他们的状况。</summary>
    public static void Notify() => Interlocked.Exchange(ref _next, Create()).TrySetResult();

    /// <summary>等待到 <paramref name="condition"/> 搁置。</summary>
    /// <exception cref="TimeoutException">状况没有在超时内维持</exception>
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
            // 之后的更改, 读取后完成所捕捉到的信号, 因此不会遗漏任何更改 。
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
