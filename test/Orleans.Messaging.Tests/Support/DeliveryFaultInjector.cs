using System.Collections.Concurrent;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 通过对每个 silo发件箱的传输方式进行装饰,将脚本错误输入发件箱呼叫接收器的调用中。
/// </summary>
/// <remarks>
/// <para>
/// 每次发送尝试都使用下个脚本 <see cref="DeliveryFault"/>;一旦脚本用尽, 试取
/// 设定一个错误时,从有缺陷的差 <see cref="Repeat"/> 起,或者不固定调用接收器。
/// 等明确释放或取消交货。
/// </para>
/// <para>
/// 注射器记录接收器自己的答复,包括错误隐藏给发送者的答复,所以测试
/// 能够肯定接收者所决定的,而不管发送人观察到什么。
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

    /// <summary>获取到达注射器的交付尝试次数。</summary>
    public int Executions => _executions.Value;

    /// <summary>获得调用接收器的试次数。</summary>
    public int ReceiverCalls => _receiverCalls.Value;

    /// <summary>获得接收者按完成顺序回答的结果,无论发送者是否看到。</summary>
    public IReadOnlyList<DeliveryResult> Answers
    {
        get
        {
            lock (_answers) return [.. _answers];
        }
    }

    /// <summary>获取发送者每次尝试都标记的逻辑发送时间, 顺序是开始尝试的时间。</summary>
    public IReadOnlyList<DateTimeOffset> SentTimes
    {
        get
        {
            lock (_sentTimes) return [.. _sentTimes];
        }
    }

    /// <summary>实际接收器电话的故障,而不是脚本运输制造的故障。</summary>
    public IReadOnlyList<Exception> ReceiverFailures
    {
        get
        {
            lock (_receiverFailures) return [.. _receiverFailures];
        }
    }

    /// <summary>返回将输入器的误差通过 <paramref name="inner"/>插入呼叫的传输。</summary>
    internal IOutboxTransport Wrap(IOutboxTransport inner) => new Transport(this, inner);

    /// <summary>脚本 <paramref name="faults"/> 下次交货要试一次,按顺序排列</summary>
    public void Enqueue(params DeliveryFault[] faults)
    {
        foreach (var fault in faults) _script.Enqueue(fault);
    }

    /// <summary>应用来自 <paramref name="create"/> 剧本用尽后每试一次</summary>
    public void Repeat(Func<DeliveryFault> create) => _repeat = create;

    /// <summary>脚本用尽后,让我们尝试调用接收器时不改变。</summary>
    public void StopRepeating() => _repeat = null;

    /// <summary>丢弃未尝试过的脚本错误, 停止重复。</summary>
    public void Clear()
    {
        _script.Clear();
        _repeat = null;
    }

    /// <summary>等待 <paramref name="count"/> 尝试到达注射器。</summary>
    public Task WaitForExecutionsAsync(int count) => _executions.WaitAsync(count);

    /// <summary>等待 <paramref name="count"/> 尝试呼叫接收器。</summary>
    public Task WaitForReceiverCallsAsync(int count) => _receiverCalls.WaitAsync(count);

    /// <summary>等待接收者回复 <paramref name="count"/> 并返回所有答案 到目前为止。</summary>
    public async Task<IReadOnlyList<DeliveryResult>> WaitForAnswersAsync(int count)
    {
        await _answerCount.WaitAsync(count);
        return Answers;
    }

    // 继续等待发送激活调度器,所以每个接收器的呼叫都是从发送 grain中发出。
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
                // 被遗弃的电话继续接听接收者;其回答只能通过 ReceiverAnswer 看到。
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
    /// <summary>一台柜台,服务员一到达目标就完成</summary>
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

/// <summary>各类错误 <see cref="DeliveryFaultInjector"/> 注射</summary>
public enum DeliveryFaultKind
{
    /// <summary>试运行失败,但有一个例外,没有打电话给接收器。</summary>
    Throw,

    /// <summary>试算表在不呼叫接收器的情况下结束。</summary>
    TimeOut,

    /// <summary>试返回一个回答, 不给接收者打电话。</summary>
    Answer,

    /// <summary>尝试同时呼叫接收器和时间,而通话继续。</summary>
    TimeOutInFlight,

    /// <summary>试一下会打电话给接收器 然后隐藏答案 在超时后</summary>
    LoseResponse,

    /// <summary>试一下等待解脱,然后它会拨打接收器。</summary>
    HoldBeforeCall,

    /// <summary>试一下会打电话给接收器,然后等待解脱后再返回答案。</summary>
    HoldAfterCall
}

/// <summary>
/// 试送的单一使用过失 以及测试观察和控制它的把手
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

    /// <summary>搞到这种错</summary>
    public DeliveryFaultKind Kind { get; }

    internal Exception? Exception { get; }
    internal DeliveryResult Result { get; }

    /// <summary>
    /// 获得接收器对这个错误造成的呼叫或失败的答应, 仅针对那些错误完成
    /// 呼叫接收器
    /// </summary>
    public Task<DeliveryResult> ReceiverAnswer => _answer.Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>无法与 <paramref name="exception"/>或默认的 <see cref="IOException"/> 进行试验。</summary>
    public static DeliveryFault Throw(Exception? exception = null) =>
        new(DeliveryFaultKind.Throw, exception ?? new IOException("Injected transient delivery failure."));

    /// <summary>试一次 <see cref="TimeoutException"/>,不打电话接收器。</summary>
    public static DeliveryFault TimeOut() => new(DeliveryFaultKind.TimeOut);

    /// <summary>回答试验时使用 <paramref name="result"/>,不拨打接收器。</summary>
    public static DeliveryFault Answer(DeliveryResult result) => new(DeliveryFaultKind.Answer, result: result);

    /// <summary>
    /// 调用接收器并同时调用试播次数,而呼喊在继续; <see cref="ReceiverAnswer"/> 报告
    /// 被抛弃的电话的答案。
    /// </summary>
    public static DeliveryFault TimeOutInFlight() => new(DeliveryFaultKind.TimeOutInFlight);

    /// <summary>
    /// 听机者呼唤接机者,然后由它决定,然后以一个 <see cref="TimeoutException"/>,当
    /// 接收器承诺后,回复就会丢失。
    /// </summary>
    public static DeliveryFault LoseResponse() => new(DeliveryFaultKind.LoseResponse);

    /// <summary>
    /// 在调用接收器直到 <see cref="Release"/> 或 <see cref="Fail"/>或交货之前,暂停试
    /// 取消。
    /// </summary>
    public static DeliveryFault HoldBeforeCall() => new(DeliveryFaultKind.HoldBeforeCall);

    /// <summary>
    /// 呼叫接收器,然后保持其回答直到 <see cref="Release"/> 或 <see cref="Fail"/>,或者直到交货
    /// 取消, 哪一个模式的发送器在接收器执行后停止运行 。
    /// </summary>
    public static DeliveryFault HoldAfterCall() => new(DeliveryFaultKind.HoldAfterCall);

    /// <summary>
    /// 等待直到尝试过这个错误; 对于 <see cref="DeliveryFaultKind.HoldAfterCall"/>, 直到接收者回答。
    /// </summary>
    public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>释放一个坚持的尝试继续。</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>释放一个以失败而进行 <paramref name="exception"/>的预试,但没有接收者的回答。</summary>
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
