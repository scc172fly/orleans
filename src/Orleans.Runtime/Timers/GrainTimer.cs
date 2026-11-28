using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Internal;
using Orleans.Serialization.Invocation;
using Orleans.Timers;
using Orleans.Runtime.Diagnostics;

namespace Orleans.Runtime;

internal abstract partial class GrainTimer : IGrainTimer
{
    protected static readonly TimerCallback TimerCallback = (state) => ((GrainTimer)state!).ScheduleTickOnActivation();
    protected static readonly MethodInfo InvokableMethodInfo = typeof(IGrainTimerInvoker).GetMethod(nameof(IGrainTimerInvoker.InvokeCallbackAsync), BindingFlags.Instance | BindingFlags.Public)!;
    private readonly CancellationTokenSource _cts = new();
    private readonly ITimer _timer;
    private readonly IGrainContext _grainContext;
    private readonly TimerRegistry _shared;
    private readonly LocalRequestOptions _options;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "The reusable invocation adapter borrows this timer and owns no disposable resources; the activation owns timer disposal.")]
    private readonly TimerTickInvoker _invoker;
    private TimerState _state;
    // 空闲时表示当前已设定的触发；触发已入队后表示下一次延迟。Change 将替换它。
    private TimeSpan _dueTime;
    private TimeSpan _period;

    [Flags]
    private enum TimerState : byte
    {
        Idle = 0,
        Busy = 1,
        CallbackRunning = 2,
        Cancelling = 4,
        Disposed = 8
    }

    public GrainTimer(TimerRegistry shared, IGrainContext grainContext, bool interleave, bool keepAlive)
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(grainContext);

        _options = new() { Interleave = interleave, KeepAlive = keepAlive };
        _shared = shared;
        _grainContext = grainContext;
        _dueTime = Timeout.InfiniteTimeSpan;
        _period = Timeout.InfiniteTimeSpan;
        _invoker = new(this);
        using (new ExecutionContextSuppressor())
        {
            _timer = _shared.TimeProvider.CreateTimer(TimerCallback, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    protected IGrainContext GrainContext => _grainContext;

    // 在 Change 或回调完成之后持有 _cts 锁调用。
    private bool ScheduleNextTick()
    {
        Debug.Assert(_state == TimerState.Idle);
        _timer.Change(_dueTime == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : _dueTime, Timeout.InfiniteTimeSpan);
        if (_dueTime == TimeSpan.Zero)
        {
            _state = TimerState.Busy;
            _dueTime = _period;
            return true;
        }

        return false;
    }

    private ILogger Logger => _shared.TimerLogger;

    private bool IsDisposed
    {
        get
        {
            lock (_cts)
            {
                return (_state & TimerState.Disposed) != 0;
            }
        }
    }

    [DoesNotReturn]
    private static void ThrowIncorrectGrainContext() => throw new InvalidOperationException("Current grain context differs from specified grain context.");

    [DoesNotReturn]
    private static void ThrowInvalidSchedulingContext()
    {
        throw new InvalidSchedulingContextException(
            "Current grain context is null. "
             + "Please make sure you are not trying to create a Timer from outside Orleans Task Scheduler, "
             + "which will be the case if you create it inside Task.Run.");
    }

    protected void ScheduleTickOnActivation()
    {
        lock (_cts)
        {
            // 提供程序回调可能与 Change 发生竞争。已预留的触发在完成前保持忙碌状态。
            if (_state != TimerState.Idle || _dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            _state = TimerState.Busy;
            _dueTime = _period;
        }

        QueueTickOnActivation();
    }

    // 在 _cts 锁内预留准入，但在锁外入队：激活实例关闭时在其自身锁内释放
    // 定时器。在持有 _cts 锁时获取该锁会颠倒锁顺序。
    // 入队期间的更改影响下一次触发；释放会阻止回调准入。
    private void QueueTickOnActivation()
    {
        try
        {
            _grainContext.ReceiveLocalRequest(_invoker, _options);
        }
        catch (Exception exception)
        {
            try
            {
                LogErrorScheduleTickOnActivation(Logger, exception, this);
            }
            catch
            {
                // 允许异常从底层定时器回调传播出去会导致进程崩溃。
            }
        }
    }

    protected abstract Task InvokeCallbackAsync(CancellationToken cancellationToken);

    private ValueTask<Response> InvokeGrainTimerCallbackAsync()
    {
        lock (_cts)
        {
            if ((_state & TimerState.Disposed) != 0)
            {
                return new(Response.Completed);
            }

            Debug.Assert(_state == TimerState.Busy);
            _state |= TimerState.CallbackRunning;
        }

        var completeSynchronously = true;
        try
        {
            LogTraceBeforeCallback(Logger, this);

            GrainTimerEvents.EmitTickStart(GrainContext, this);
            var task = InvokeCallbackAsync(_cts.Token);

            // 如果任务尚未完成，则需要异步等待此次触发。
            if (task is { IsCompletedSuccessfully: false })
            {
                // 异步完成。
                completeSynchronously = false;
                return AwaitCallbackTask(task);
            }
            else
            {
                // 同步完成。
                LogTraceAfterCallback(Logger, this);

                GrainTimerEvents.EmitTickStop(GrainContext, this);
                return new(Response.Completed);
            }
        }
        catch (Exception exc)
        {
            GrainTimerEvents.EmitTickStop(GrainContext, this, exc);
            return new(Response.FromException(exc));
        }
        finally
        {
            if (completeSynchronously)
            {
                OnTickCompleted();
            }
        }
    }

    private void OnTickCompleted()
    {
        bool queueTick;
        lock (_cts)
        {
            ReleaseCancellationSource(TimerState.CallbackRunning);
            if ((_state & TimerState.Disposed) != 0)
            {
                return;
            }

            Debug.Assert(_state == TimerState.Busy);
            _state = TimerState.Idle;
            queueTick = ScheduleNextTick();
        }

        if (queueTick)
        {
            QueueTickOnActivation();
        }
    }

    // 取消操作和已准入的回调分别持有取消令牌源，直至各自完成。
    private void ReleaseCancellationSource(TimerState owner)
    {
        Debug.Assert(Monitor.IsEntered(_cts));
        Debug.Assert((_state & owner) != 0);
        _state &= ~owner;
        if (_state == TimerState.Disposed)
        {
            _cts.Dispose();
        }
    }

    private async ValueTask<Response> AwaitCallbackTask(Task task)
    {
        try
        {
            await task;

            LogTraceAfterCallback(Logger, this);

            GrainTimerEvents.EmitTickStop(GrainContext, this);

            return Response.Completed;
        }
        catch (Exception exc)
        {
            GrainTimerEvents.EmitTickStop(GrainContext, this, exc);

            return Response.FromException(exc);
        }
        finally
        {
            OnTickCompleted();
        }
    }

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        ValidateArguments(dueTime, period);

        var queueTick = false;
        lock (_cts)
        {
            if ((_state & TimerState.Disposed) != 0)
            {
                return;
            }

            _dueTime = dueTime;
            _period = period;

            // 已排队或正在运行的回调保留其执行轮次；更改会在它完成后安排工作。
            if (_state == TimerState.Idle)
            {
                queueTick = ScheduleNextTick();
            }
        }

        if (queueTick)
        {
            QueueTickOnActivation();
        }
    }

    private static void ValidateArguments(TimeSpan dueTime, TimeSpan period)
    {
        // 参见：https://github.com/dotnet/runtime/blob/78b5f40a60d9e095abb2b0aabd8c062b171fb9ab/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs#L824-L825
        const uint MaxSupportedTimeout = 0xfffffffe;

        // 参见：https://github.com/dotnet/runtime/blob/78b5f40a60d9e095abb2b0aabd8c062b171fb9ab/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs#L927-L930
        long dueTm = (long)dueTime.TotalMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(dueTm, -1, nameof(dueTime));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTm, MaxSupportedTimeout, nameof(dueTime));

        long periodTm = (long)period.TotalMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(periodTm, -1, nameof(period));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(periodTm, MaxSupportedTimeout, nameof(period));
    }

    public void Dispose()
    {
        lock (_cts)
        {
            if ((_state & TimerState.Disposed) != 0)
            {
                return;
            }

            // 在取消之前发布已释放状态，因为取消注册的回调可能重入 Change 或 Dispose。
            _state = (_state & TimerState.CallbackRunning) | TimerState.Disposed | TimerState.Cancelling;
            _timer.Dispose();
        }

        try
        {
            // 不要在定时器的状态锁内执行取消注册的回调。
            _cts.Cancel();
        }
        catch (Exception exception)
        {
            LogErrorCancellingCallback(Logger, exception);
        }
        finally
        {
            lock (_cts)
            {
                ReleaseCancellationSource(TimerState.Cancelling);
            }
        }

        GrainTimerEvents.EmitDisposed(GrainContext, this);

        var timerRegistry = _grainContext.GetComponent<IGrainTimerRegistry>();
        timerRegistry?.OnTimerDisposed(this);
    }

    public override string ToString() => $"[{GetType()}] Grain: '{_grainContext}'";

    private sealed class TimerTickInvoker(GrainTimer timer) : ILocalRequest, IGrainTimerInvoker
    {
        public object? GetTarget() => this;

        public void SetTarget(ITargetHolder holder)
        {
            if (timer._grainContext != holder)
            {
                throw new InvalidOperationException($"Invalid target holder. Expected {timer._grainContext}, received {holder}.");
            }
        }

        public ValueTask<Response> Invoke() => timer.InvokeGrainTimerCallbackAsync();

        // 没有调用方观察定时器触发，因此由触发操作记录其失败，包括调用筛选器失败。
        // 激活实例先释放其定时器，再拒绝已排队的触发，因此已释放定时器的失败属于预期情况。
        public void Complete(Response response)
        {
            if (response.Exception is { } exception && !timer.IsDisposed)
            {
                LogWarningCallbackException(timer.Logger, exception, timer);
            }
        }

        // 为满足 IGrainTimerCore 而声明此方法，但不应直接调用它。
        // 它用于检查实现方法的 Grain 调用拦截器。
        Task IGrainTimerInvoker.InvokeCallbackAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();

        public int GetArgumentCount() => 0;

        public object? GetArgument(int index) => throw new InvalidOperationException();

        public void SetArgument(int index, object? value) => throw new InvalidOperationException();

        public string GetMethodName() => nameof(IGrainTimerInvoker.InvokeCallbackAsync);

        public string GetInterfaceName() => nameof(IGrainTimerInvoker);

        public string GetActivityName() => $"{nameof(IGrainTimerInvoker)}/{nameof(IGrainTimerInvoker.InvokeCallbackAsync)}";

        public MethodInfo GetMethod() => InvokableMethodInfo;

        public Type GetInterfaceType() => typeof(IGrainTimerInvoker);

        public TimeSpan? GetDefaultResponseTimeout() => null;

        public void Dispose()
        {
            // 不执行任何操作。实例在调用后通常被释放，但此实例在定时器的整个生命周期内复用。
        }

        public override string ToString() => timer.ToString();
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error invoking timer tick for timer '{Timer}'."
    )]
    private static partial void LogErrorScheduleTickOnActivation(ILogger logger, Exception exception, GrainTimer timer);

    [LoggerMessage(
        EventId = (int)ErrorCode.TimerBeforeCallback,
        Level = LogLevel.Trace,
        Message = "About to invoke callback for timer {Timer}"
    )]
    private static partial void LogTraceBeforeCallback(ILogger logger, GrainTimer timer);

    [LoggerMessage(
        EventId = (int)ErrorCode.TimerAfterCallback,
        Level = LogLevel.Trace,
        Message = "Completed timer callback for timer '{Timer}'."
    )]
    private static partial void LogTraceAfterCallback(ILogger logger, GrainTimer timer);

    [LoggerMessage(
        EventId = (int)ErrorCode.Timer_GrainTimerCallbackError,
        Level = LogLevel.Warning,
        Message = "Caught and ignored exception thrown from timer tick for timer '{Timer}'."
    )]
    private static partial void LogWarningCallbackException(ILogger logger, Exception exception, GrainTimer timer);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error cancelling timer callback."
    )]
    private static partial void LogErrorCancellingCallback(ILogger logger, Exception exception);
}

internal sealed class GrainTimer<T> : GrainTimer
{
    private readonly Func<T, CancellationToken, Task> _callback;
    private readonly T _state;

    public GrainTimer(
        TimerRegistry shared,
        IGrainContext grainContext,
        Func<T, CancellationToken, Task> callback,
        T state,
        bool interleave,
        bool keepAlive)
        : base(
            shared,
            grainContext,
            interleave,
            keepAlive)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        _state = state;
    }

    protected override Task InvokeCallbackAsync(CancellationToken cancellationToken) => _callback(_state, cancellationToken);

    public override string ToString() => $"{base.ToString()} Callback: '{_callback?.Target}.{_callback?.Method}'. State: '{_state}'";
}

internal sealed class InterleavingGrainTimer : GrainTimer
{
    private readonly Func<object?, Task> _callback;
    private readonly object? _state;

    public InterleavingGrainTimer(
        TimerRegistry shared,
        IGrainContext grainContext,
        Func<object?, Task> callback,
        object? state)
        : base(
            shared,
            grainContext,
            interleave: true,
            keepAlive: false)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        _state = state;
    }

    protected override Task InvokeCallbackAsync(CancellationToken cancellationToken) => _callback(_state);

    public override string ToString() => $"{base.ToString()} Callback: '{_callback?.Target}.{_callback?.Method}'. State: '{_state}'";
}

// 此接口供 IInvokable 实现使用，使调用筛选器按预期工作。
internal interface IGrainTimerInvoker : IAddressable
{
    /// <summary>
    /// 调用回调。
    /// </summary>
    [Alias("3F6C2672")]
    Task InvokeCallbackAsync(CancellationToken cancellationToken = default);
}
