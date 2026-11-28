using System.Collections.Concurrent;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 通过装饰每个 Silo 的发件箱传输实现，向发件箱对接收方的调用注入预设故障。
/// </summary>
/// <remarks>
/// <para>
/// 每次投递尝试取下一个预设的 <see cref="DeliveryFault"/>；预设序列耗尽后，尝试会从
/// <see cref="Repeat"/> 获取故障（如果已设置），否则正常调用接收方。故障不等待时间
/// 流逝：超时会立即报告，阻塞则等待显式释放或投递取消。
/// </para>
/// <para>
/// 注入器记录接收方自身的答复，包括因故障而未被发送方看到的答复，因此测试
/// 可独立于发送方所观察到的结果，对接收方的实际决定进行断言。
/// </para>
/// </remarks>
public sealed class DeliveryFaultInjector
{
    private readonly ConcurrentQueue<DeliveryFault> _script = new();
    private readonly CountSignal _executions = new();
    private readonly CountSignal _receiverCalls = new();
    private readonly CountSignal _answerCount = new();
    private readonly List<DeliveryResult> _answers = [];
    private readonly List<Exception> _receiverFailures = [];
    private readonly List<DateTimeOffset> _sentTimes = [];
    private Func<DeliveryFault>? _repeat;

    /// <summary>获取已到达注入器的投递尝试次数。</summary>
    public int Executions => _executions.Value;

    /// <summary>获取已调用接收方的尝试次数。</summary>
    public int ReceiverCalls => _receiverCalls.Value;

    /// <summary>按完成顺序获取接收方答复的结果，不论发送方是否收到。</summary>
    public IReadOnlyList<DeliveryResult> Answers
    {
        get
        {
            lock (_answers) return [.. _answers];
        }
    }

    /// <summary>按尝试启动顺序获取发送方为各次尝试标记的逻辑发送时间。</summary>
    public IReadOnlyList<DateTimeOffset> SentTimes
    {
        get
        {
            lock (_sentTimes) return [.. _sentTimes];
        }
    }

    /// <summary>获取实际接收方调用抛出的失败，而非由预设故障传输实现构造的失败。</summary>
    public IReadOnlyList<Exception> ReceiverFailures
    {
        get
        {
            lock (_receiverFailures) return [.. _receiverFailures];
        }
    }

    /// <summary>返回一个传输实现，将此注入器的故障注入通过 <paramref name="inner"/> 发起的调用。</summary>
    internal IOutboxTransport Wrap(IOutboxTransport inner) => new Transport(this, inner);

    /// <summary>为接下来的投递尝试依次预设 <paramref name="faults"/>。</summary>
    public void Enqueue(params DeliveryFault[] faults)
    {
        foreach (var fault in faults) _script.Enqueue(fault);
    }

    /// <summary>预设序列耗尽后，对每次尝试应用来自 <paramref name="create"/> 的新故障。</summary>
    public void Repeat(Func<DeliveryFault> create) => _repeat = create;

    /// <summary>预设序列耗尽后，允许尝试正常调用接收方。</summary>
    public void StopRepeating() => _repeat = null;

    /// <summary>丢弃尚未被任何尝试取用的预设故障，并停止重复注入。</summary>
    public void Clear()
    {
        _script.Clear();
        _repeat = null;
    }

    /// <summary>等待 <paramref name="count"/> 次尝试到达注入器。</summary>
    public Task WaitForExecutionsAsync(int count) => _executions.WaitAsync(count);

    /// <summary>等待 <paramref name="count"/> 次尝试调用接收方。</summary>
    public Task WaitForReceiverCallsAsync(int count) => _receiverCalls.WaitAsync(count);

    /// <summary>等待接收方答复 <paramref name="count"/> 次调用，并返回截至目前的所有答复。</summary>
    public async Task<IReadOnlyList<DeliveryResult>> WaitForAnswersAsync(int count)
    {
        await _answerCount.WaitAsync(count);
        return Answers;
    }

    // await 的延续在发送方激活实例的调度器上执行，因此每次接收方调用均从发送方 Grain 发起。
    private ValueTask<DeliveryResult> DeliverAsync(IOutboxTransport inner, GrainId receiver, InboxMessage message, CancellationToken cancellationToken)
    {
        lock (_sentTimes) _sentTimes.Add(message.SentAt);
        _executions.Increment();
        var fault = _script.TryDequeue(out var scripted) ? scripted : _repeat?.Invoke();
        return fault is null
            ? CallReceiverAsync(inner, receiver, message, cancellationToken, null)
            : PlayAsync(fault, inner, receiver, message, cancellationToken);
    }

    private async ValueTask<DeliveryResult> PlayAsync(
        DeliveryFault fault, IOutboxTransport inner, GrainId receiver, InboxMessage message, CancellationToken cancellationToken)
    {
        if (fault.Kind != DeliveryFaultKind.HoldAfterCall) fault.OnEntered();
        switch (fault.Kind)
        {
            case DeliveryFaultKind.Throw:
                throw fault.Exception!;
            case DeliveryFaultKind.TimeOut:
                throw new TimeoutException("Injected delivery timeout before the receiver was called.");
            case DeliveryFaultKind.Answer:
                return fault.Result;
            case DeliveryFaultKind.TimeOutInFlight:
                // 已放弃等待的调用仍继续到达接收方；只能通过 ReceiverAnswer 观察其答复。
                CallReceiverAsync(inner, receiver, message, cancellationToken, fault).AsTask().Ignore();
                throw new TimeoutException("Injected delivery timeout while the receiver call was in flight.");
            case DeliveryFaultKind.LoseResponse:
                try
                {
                    await CallReceiverAsync(inner, receiver, message, cancellationToken, fault);
                }
                catch
                {
                }

                throw new TimeoutException("Injected loss of the receiver's response.");
            case DeliveryFaultKind.HoldBeforeCall:
                await fault.WaitForReleaseAsync(cancellationToken);
                return await CallReceiverAsync(inner, receiver, message, cancellationToken, fault);
            case DeliveryFaultKind.HoldAfterCall:
                var answer = await CallReceiverAsync(inner, receiver, message, cancellationToken, fault);
                fault.OnEntered();
                await fault.WaitForReleaseAsync(cancellationToken);
                return answer;
            default:
                throw new InvalidOperationException($"Unknown delivery fault '{fault.Kind}'.");
        }
    }

    private async ValueTask<DeliveryResult> CallReceiverAsync(
        IOutboxTransport inner, GrainId receiver, InboxMessage message, CancellationToken cancellationToken, DeliveryFault? fault)
    {
        _receiverCalls.Increment();
        DeliveryResult result;
        try
        {
            result = await inner.DeliverAsync(receiver, message, cancellationToken);
        }
        catch (Exception exception)
        {
            lock (_receiverFailures) _receiverFailures.Add(exception);
            fault?.OnAnswered(exception);
            throw;
        }

        lock (_answers) _answers.Add(result);
        _answerCount.Increment();
        fault?.OnAnswered(result);
        return result;
    }

    private sealed class Transport(DeliveryFaultInjector owner, IOutboxTransport inner) : IOutboxTransport
    {
        public ValueTask<DeliveryResult> DeliverAsync(GrainId receiver, InboxMessage message, CancellationToken cancellationToken) =>
            owner.DeliverAsync(inner, receiver, message, cancellationToken);
    }
    /// <summary>一个计数器，当计数达到等待者的目标时，相应等待即完成。</summary>
    private sealed class CountSignal
    {
        private readonly List<(int Target, TaskCompletionSource Signal)> _waiters = [];
        private int _value;

        public int Value
        {
            get
            {
                lock (_waiters) return _value;
            }
        }

        public void Increment()
        {
            List<TaskCompletionSource>? ready = null;
            lock (_waiters)
            {
                _value++;
                for (var index = _waiters.Count - 1; index >= 0; index--)
                {
                    if (_waiters[index].Target > _value) continue;
                    (ready ??= []).Add(_waiters[index].Signal);
                    _waiters.RemoveAt(index);
                }
            }

            ready?.ForEach(static signal => signal.TrySetResult());
        }

        public Task WaitAsync(int target)
        {
            lock (_waiters)
            {
                if (_value >= target) return Task.CompletedTask;
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((target, signal));
                return signal.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
    }
}

/// <summary><see cref="DeliveryFaultInjector"/> 所注入的故障类型。</summary>
public enum DeliveryFaultKind
{
    /// <summary>尝试因异常失败，不调用接收方。</summary>
    Throw,

    /// <summary>尝试超时，不调用接收方。</summary>
    TimeOut,

    /// <summary>尝试返回答复，不调用接收方。</summary>
    Answer,

    /// <summary>尝试调用接收方后立即超时，而调用本身仍继续。</summary>
    TimeOutInFlight,

    /// <summary>尝试调用接收方，然后以超时掩盖其答复。</summary>
    LoseResponse,

    /// <summary>尝试等待释放后才调用接收方。</summary>
    HoldBeforeCall,

    /// <summary>尝试调用接收方，再等待释放后返回答复。</summary>
    HoldAfterCall
}

/// <summary>
/// 用于一次投递尝试的单次使用故障，以及测试用来观察和控制该故障的句柄。
/// </summary>
public sealed class DeliveryFault
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<DeliveryResult> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _used;

    private DeliveryFault(DeliveryFaultKind kind, Exception? exception = null, DeliveryResult result = default)
    {
        Kind = kind;
        Exception = exception;
        Result = result;
    }

    /// <summary>获取此故障的类型。</summary>
    public DeliveryFaultKind Kind { get; }

    internal Exception? Exception { get; }
    internal DeliveryResult Result { get; }

    /// <summary>
    /// 获取接收方对该故障发起的调用的答复或失败。仅当故障
    /// 实际调用接收方时才会完成。
    /// </summary>
    public Task<DeliveryResult> ReceiverAnswer => _answer.Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>使尝试因 <paramref name="exception"/> 失败，默认使用 <see cref="IOException"/>。</summary>
    public static DeliveryFault Throw(Exception? exception = null) =>
        new(DeliveryFaultKind.Throw, exception ?? new IOException("Injected transient delivery failure."));

    /// <summary>使用 <see cref="TimeoutException"/> 使尝试超时，而不调用接收方。</summary>
    public static DeliveryFault TimeOut() => new(DeliveryFaultKind.TimeOut);

    /// <summary>使用 <paramref name="result"/> 答复尝试，而不调用接收方。</summary>
    public static DeliveryFault Answer(DeliveryResult result) => new(DeliveryFaultKind.Answer, result: result);

    /// <summary>
    /// 调用接收方并立即使尝试超时，而调用仍继续；<see cref="ReceiverAnswer"/> 报告
    /// 已放弃等待的调用的答复。
    /// </summary>
    public static DeliveryFault TimeOutInFlight() => new(DeliveryFaultKind.TimeOutInFlight);

    /// <summary>
    /// 调用接收方并让其作出决定，然后使尝试因 <see cref="TimeoutException"/> 失败，用于模拟
    /// 接收方提交后响应丢失的情况。
    /// </summary>
    public static DeliveryFault LoseResponse() => new(DeliveryFaultKind.LoseResponse);

    /// <summary>
    /// 在尝试调用接收方之前将其阻塞，直到 <see cref="Release"/> 或 <see cref="Fail"/>，或者投递
    /// 被取消。
    /// </summary>
    public static DeliveryFault HoldBeforeCall() => new(DeliveryFaultKind.HoldBeforeCall);

    /// <summary>
    /// 调用接收方后阻塞其答复，直到 <see cref="Release"/> 或 <see cref="Fail"/>，或者投递
    /// 被取消；用于模拟发送方在接收方提交后停止的情况。
    /// </summary>
    public static DeliveryFault HoldAfterCall() => new(DeliveryFaultKind.HoldAfterCall);

    /// <summary>
    /// 等待一次尝试取用此故障；对于 <see cref="DeliveryFaultKind.HoldAfterCall"/>，则等待接收方答复。
    /// </summary>
    public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>释放被阻塞的尝试，使其继续执行。</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>通过使被阻塞的尝试因 <paramref name="exception"/> 失败来释放它，不返回接收方的答复。</summary>
    public void Fail(Exception exception) => _released.TrySetException(exception);

    internal void OnEntered()
    {
        if (Interlocked.Exchange(ref _used, 1) != 0)
        {
            throw new InvalidOperationException("A delivery fault applies to one try only.");
        }

        _entered.TrySetResult();
    }

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _released.Task.WaitAsync(cancellationToken);

    internal void OnAnswered(DeliveryResult result) => _answer.TrySetResult(result);

    internal void OnAnswered(Exception exception) => _answer.TrySetException(exception);
}
