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
    // 当前臂空闲时的当前臂; 键被排队后下次延迟。 更改替换它 。
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

    // 与(_c)连接,在更改或回调完成后锁定。
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
            // 提供方回调可能会在跑步中变换。 保留一个选中的点会一直忙到完成为止 。
            if (_state != TimerState.Idle || _dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            _state = TimerState.Busy;
            _dueTime = _period;
        }

        QueueTickOnActivation();
    }

    // 入场量保留在 _cts下,但排在外:启动关闭处置
    // 自动锁定时器。在锁上锁时, 持有 _cts 就会反转锁定命令 。
    // 排队过程中的变化会影响以下勾;处置可防止回调。
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
                // 允许例外以逃避物理定时回击会中断进程 。
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

            // 如果任务尚未完成,我们需要不时地等待这一时刻。
            if (task is { IsCompletedSuccessfully: false })
            {
                // 完全无与伦比
                completeSynchronously = false;
                return AwaitCallbackTask(task);
            }
            else
            {
                // 完全同步
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

    // 取消和一次被接受的回召,每个都保留来源直至完成。
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

            // 排队或回调保持其转弯;完成后更改时间表。
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
        // 见 https://github.com/dotnet/runtime/blob/78b5f40a60d9e095abb2b0aabd8c062b171fb9ab/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs#L824-L825
        const uint MaxSupportedTimeout = 0xfffffffe;

        // 见 https://github.com/dotnet/runtime/blob/78b5f40a60d9e095abb2b0aabd8c062b171fb9ab/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs#L927-L930
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

            // 注销前公布处置情况,其登记可重新进入变更/销毁。
            _state = (_state & TimerState.CallbackRunning) | TimerState.Disposed | TimerState.Cancelling;
            _timer.Dispose();
        }

        try
        {
            // 不要在计时器国家锁下运行取消登记 。
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

        // 没有呼叫器观察一个滴答, 所以选中键记录失败, 包括拨号过滤器失败 。
        // 激活程序在它拒绝它们排队的滴答之前会处理它的计时器, 所以预计被处理的定时器会出故障。
        public void Complete(Response response)
        {
            if (response.Exception is { } exception && !timer.IsDisposed)
            {
                LogWarningCallbackException(timer.Logger, exception, timer);
            }
        }

        // 宣布采用这种方法是为了 IGrainTimerCore,但无意直接称为。
        // 用于检查执行方法的 grain 呼叫拦截器。
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
            // 无。 案例在援引后处理, 但这个实例将重新使用到定时器的寿命中 。
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

// 此界面用于 IInvokable 执行, 这样呼叫过滤器就会按预期运行 。
internal interface IGrainTimerInvoker : IAddressable
{
    /// <summary>
    /// 调回电源
    /// </summary>
    [Alias("3F6C2672")]
    Task InvokeCallbackAsync(CancellationToken cancellationToken = default);
}
