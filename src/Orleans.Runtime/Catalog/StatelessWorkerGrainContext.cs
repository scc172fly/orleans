using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Diagnostics;

namespace Orleans.Runtime;

internal partial class StatelessWorkerGrainContext : IGrainContext, IAsyncDisposable, IActivationLifecycleObserver
{
    private static readonly object CollectIdleWorkersSentinel = new();
    private readonly StatelessWorkerGrainTypeSharedContext _shared;
    private readonly IGrainContextActivator _innerActivator;
    private readonly List<ActivationData> _workers = [];
    private readonly ConcurrentQueue<(WorkItemType Type, object State)> _workItems = new();
    private readonly SingleWaiterAutoResetEvent _workSignal = new() { RunContinuationsAsynchronously = false };
    private readonly TaskCompletionSource _disposalTask = new(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable IDE0052 // Remove unread private members
    /// <summary>
    /// 下 <see cref="Task"/> 代表《公约》 <see cref="RunMessageLoop"/> 请求
    /// 本文仅写此,但绝无其他查阅途径。
    /// 调试, 当能够识别与激活相对应的电文循环任务时可以
    /// 有用。
    /// </summary>
    private readonly Task _messageLoopTask;
#pragma warning restore IDE0052 // Remove unread private members

    private bool _terminated;

    // 空闲工人搬运田
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "DisposeAsyncInternal clears this field and awaits Timer.DisposeAsync before completing context disposal.")]
    private Timer? _inspectionTimer;
    private double _previousError;
    private double _integralTerm;
    private int _detectedIdleCyclesCount;

    public StatelessWorkerGrainContext(
        GrainAddress address,
        StatelessWorkerGrainTypeSharedContext sharedContext,
        IGrainContextActivator innerActivator,
        IConfigureGrainContext[] configureActions)
    {
        Address = address;
        _shared = sharedContext;
        _innerActivator = innerActivator;

        foreach (var configure in configureActions)
        {
            configure.Configure(this);
        }

        if (_shared.RemoveIdleWorkers)
        {
            _inspectionTimer = new Timer(
                static state => ((StatelessWorkerGrainContext)state!).EnqueueWorkItem(WorkItemType.CollectIdleWorkers, CollectIdleWorkersSentinel),
                this,
                _shared.IdleWorkersInspectionPeriod,
                _shared.IdleWorkersInspectionPeriod);
        }

        _messageLoopTask = Task.Run(RunMessageLoop);
    }

    public GrainReference GrainReference => field ??= _shared.Shared.GrainReferenceActivator.CreateReference(GrainId, default);

    public GrainId GrainId => Address.GrainId;

    public object? GrainInstance => null;

    public ActivationId ActivationId => Address.ActivationId;

    public GrainAddress Address { get; }

    public IServiceProvider ActivationServices => throw new NotImplementedException();

    public IGrainLifecycle ObservableLifecycle => throw new NotImplementedException();

    public IWorkItemScheduler Scheduler => throw new NotImplementedException();

    public PlacementStrategy PlacementStrategy => _shared.Shared.PlacementStrategy;

    internal GrainTypeMetrics GrainTypeMetrics => _shared.Shared.GrainTypeMetrics;

    public Task Deactivated
    {
        get
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueWorkItem(WorkItemType.DeactivatedTask, new DeactivatedTaskWorkItemState(completion));
            return completion.Task;
        }
    }

    public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) { }

    public void ReceiveMessage(object message) => EnqueueWorkItem(WorkItemType.Message, message);

    public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken) =>
        EnqueueWorkItem(WorkItemType.Deactivate, new DeactivateWorkItemState(deactivationReason, cancellationToken));

    public async ValueTask DisposeAsync()
    {
        EnqueueWorkItem(WorkItemType.DisposeAsync, DisposeAsyncWorkItemState.Instance);
        await _disposalTask.Task;
    }

    private void EnqueueWorkItem(WorkItemType type, object state)
    {
        _workItems.Enqueue(new(type, state));
        _workSignal.Signal();
    }

    public bool Equals([AllowNull] IGrainContext other) => other is not null && ActivationId.Equals(other.ActivationId);

    public object? GetComponent(Type componentType)
    {
        if (componentType.IsAssignableFrom(GetType())) return this;
        return _shared.Shared.GetComponent(componentType);
    }

    public void SetComponent<TComponent>(TComponent? instance) where TComponent : class
    {
        if (typeof(TComponent) != typeof(GrainCanInterleave))
        {
            throw new ArgumentException($"Cannot set a component of type '{typeof(TComponent)}' on a {nameof(StatelessWorkerGrainContext)}");
        }

        _shared.Shared.SetComponent(instance);
    }

    public object? GetTarget() => throw new NotImplementedException();

    private async Task RunMessageLoop()
    {
        while (true)
        {
            try
            {
                while (_workItems.TryDequeue(out var workItem))
                {
                    switch (workItem.Type)
                    {
                        case WorkItemType.Message:
                            ReceiveMessageInternal(workItem.State);
                            break;
                        case WorkItemType.Deactivate:
                            {
                                var state = (DeactivateWorkItemState)workItem.State;
                                DeactivateInternal(state.DeactivationReason, state.CancellationToken);
                                break;
                            }
                        case WorkItemType.DeactivatedTask:
                            {
                                var state = (DeactivatedTaskWorkItemState)workItem.State;
                                _ = DeactivatedTaskInternal(state.Completion);
                                break;
                            }
                        case WorkItemType.DisposeAsync:
                            {
                                _ = DisposeAsyncInternal();
                                break;
                            }
                        case WorkItemType.OnDestroyActivation:
                            {
                                var grainContext = (ActivationData)workItem.State;
                                _workers.Remove(grainContext);

                                if (_workers.Count == 0)
                                {
                                    Debug.Assert(!_terminated, "OnDestroyActivation should only terminate the context once.");
                                    _terminated = true;
                                    _shared.Shared.InternalRuntime.Catalog.UnregisterMessageTarget(this);
                                    StatelessWorkerEvents.EmitContextTerminated(this, _workers.Count);

                                    if (_shared.RemoveIdleWorkers)
                                    {
                                        EnqueueWorkItem(WorkItemType.DisposeAsync, DisposeAsyncWorkItemState.Instance);
                                    }
                                }
                                break;
                            }
                        case WorkItemType.CollectIdleWorkers:
                            CollectIdleWorkers();
                            break;
                        default:
                            throw new NotSupportedException($"Work item of type {workItem.Type} is not supported");
                    }
                }

                await _workSignal.WaitAsync();
            }
            catch (Exception exception)
            {
                LogErrorInMessageLoop(_shared.Shared.Logger, exception);
            }
        }
    }

    private void CollectIdleWorkers()
    {
        // 这些参数值是使用基因算法调整的,可能进行重新校正时见:
        // https://github.com/ledjon-behluli/Orleans.FullyAdaptiveStatelessWorkerSimulations/blob/main/Orleans.FullyAdaptiveStatelessWorkerSimulations/Program.cs

        // 更多信息见:
        // https://www.ledjonbehluli.com/posts/orleans_adaptive_stateless_worker/

        const double Kp = 0.433;
        const double Ki = 0.468;
        const double Kd = 0.480;

        var totalWaitingCount = 0L;
        foreach (var worker in _workers)
        {
            totalWaitingCount += worker.GetRequestStatus().WaitingCount;
        }

        var averageWaitingCount = _workers.Count > 0 ? (double)totalWaitingCount / _workers.Count : 0d;
        var error = -averageWaitingCount; // 我们的目标是等待0个计数: 0 - avgWC = -avgWC

        _integralTerm += error;
        var derivativeTerm = error - _previousError;
        _previousError = error;

        var controlSignal = Kp * error + Ki * _integralTerm + Kd * derivativeTerm;

        _detectedIdleCyclesCount = controlSignal < 0 ? ++_detectedIdleCyclesCount : 0;

        if (_detectedIdleCyclesCount >= _shared.MinIdleCyclesBeforeRemoval)
        {
            var inactiveWorkers = _workers.Where(static worker => worker.GetRequestStatus().IsInactive).ToImmutableArray();
            if (inactiveWorkers.Length > 0)
            {
                var worker = inactiveWorkers[Random.Shared.Next(inactiveWorkers.Length)];
                worker.Deactivate(new(DeactivationReasonCode.RuntimeRequested, "Worker deactivated due to inactivity."));

                var antiWindUpFactor = (double)(inactiveWorkers.Length - 1) / inactiveWorkers.Length;

                _integralTerm *= antiWindUpFactor;
                _detectedIdleCyclesCount = 0;
            }
        }
    }

    private void ReceiveMessageInternal(object message)
    {
        IGrainContext? destination;
        try
        {
            destination = _terminated ? GetReplacementContext((Message)message) : GetWorker(message);
        }
        catch (Exception exception) when (message is Message msg)
        {
            _shared.Shared.InternalRuntime.MessageCenter.RejectMessage(
                msg,
                Message.RejectionTypes.Transient,
                exception,
                "Exception while creating grain context");
            return;
        }

        if (destination is null)
        {
            _shared.Shared.InternalRuntime.MessageCenter.RejectMessage(
                (Message)message, Message.RejectionTypes.Transient, null, "The silo is no longer creating activations.");
            return;
        }

        // 根据该电话进行所有权转让,包括目的地报告失败。
        destination.ReceiveMessage(message);
    }

    private ActivationData GetWorker(object message)
    {
        if (_workers.Count == 0)
        {
            return CreateWorker(message);
        }

        ActivationData? minimumWaitingCountWorker = null;
        var minimumWaitingCount = int.MaxValue;
        for (var i = 0; i < _workers.Count; i++)
        {
            var candidate = _workers[i];
            var status = candidate.GetRequestStatus();
            if (status.IsInactive)
            {
                return candidate;
            }

            if (status.WaitingCount < minimumWaitingCount)
            {
                minimumWaitingCount = status.WaitingCount;
                minimumWaitingCountWorker = candidate;
            }
        }

        return _workers.Count >= _shared.MaxLocalWorkers ? minimumWaitingCountWorker! : CreateWorker(message);
    }

    private IGrainContext? GetReplacementContext(Message message)
    {
        // 目录无法返回此上下文, 因为之前我们设置了(_ kdate)
        // 解除登记,终止的背景从未重新登记。
        // 由目录返回的替换是同一 grain id的新上下文。
        Debug.Assert(_terminated);
        var replacement = _shared.Shared.InternalRuntime.Catalog.GetOrCreateActivation(
            GrainId,
            message.RequestContextData ?? []);
        Debug.Assert(!ReferenceEquals(replacement, this), "Catalog must not resolve to a terminated stateless worker context.");
        if (replacement is not null)
        {
            StatelessWorkerEvents.EmitMessageForwarded(this, replacement, message);
        }

        return replacement;
    }

    private ActivationData CreateWorker(object? message)
    {
        Debug.Assert(!_terminated, "CreateWorker must not be called on a terminated stateless worker context.");
        var address = GrainAddress.GetAddress(Address.SiloAddress, Address.GrainId, ActivationId.NewId());
        var newWorker = (ActivationData)_innerActivator.CreateContext(address, []);

        // 观察激活的创建/销毁生命周期
        newWorker.SetComponent<IActivationLifecycleObserver>(this);

        // 如果这是一个新的工人, 且在范围上有一个信息, 尝试获取请求背景并激活该工人
        var requestContext = (message as Message)?.RequestContextData ?? [];
        // ActivationData 拥有并处置激活超时。
        newWorker.Activate(requestContext, CancellationToken.None);
        _workers.Add(newWorker);
        StatelessWorkerEvents.EmitWorkerCreated(this, newWorker, _workers.Count);

        return newWorker;
    }

    private void DeactivateInternal(DeactivationReason reason, CancellationToken cancellationToken)
    {
        foreach (var worker in _workers)
        {
            worker.Deactivate(reason, cancellationToken);
        }
    }

    private async Task DeactivatedTaskInternal(TaskCompletionSource completion)
    {
        try
        {
            var tasks = new List<Task>(_workers.Count);
            foreach (var worker in _workers)
            {
                tasks.Add(worker.Deactivated);
            }

            await Task.WhenAll(tasks);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DisposeAsyncInternal()
    {
        try
        {
            if (_inspectionTimer is { } inspectionTimer)
            {
                _inspectionTimer = null;
                await inspectionTimer.DisposeAsync().AsTask()
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            var tasks = new List<Task>(_workers.Count);
            var deactivationReason = new DeactivationReason(DeactivationReasonCode.RuntimeRequested, "Stateless worker grain context is being disposed.");
            foreach (var worker in _workers)
            {
                try
                {
                    worker.Deactivate(deactivationReason);
                    tasks.Add(worker.Deactivated);
                }
                catch (Exception exception)
                {
                    tasks.Add(Task.FromException(exception));
                }
            }

            await Task.WhenAll(tasks);
            _disposalTask.TrySetResult();
        }
        catch (Exception exception)
        {
            LogErrorInDisposeAsync(_shared.Shared.Logger, exception);
            _disposalTask.TrySetException(exception);
        }
    }

    public void OnCreateActivation(IGrainContext grainContext)
    {
    }

    public void OnDestroyActivation(IGrainContext grainContext) =>
        EnqueueWorkItem(WorkItemType.OnDestroyActivation, grainContext);

    public void Rehydrate(IRehydrationContext context) =>
        // 移民得不到支持,但是如果有背景情况的话,我们需要处理它。
        (context as IDisposable)?.Dispose();

    public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
    {
        // 移民得不到支持,什么都不要做:合同是这种方式试图移徙,但不能保证移徙会发生。
    }

    private enum WorkItemType
    {
        Message,
        Deactivate,
        DeactivatedTask,
        DisposeAsync,
        OnDestroyActivation,
        CollectIdleWorkers
    }

    private record ActivateWorkItemState(Dictionary<string, object>? RequestContext, CancellationToken CancellationToken);
    private record DeactivateWorkItemState(DeactivationReason DeactivationReason, CancellationToken CancellationToken);
    private record DeactivatedTaskWorkItemState(TaskCompletionSource Completion);
    private record DisposeAsyncWorkItemState { public static readonly DisposeAsyncWorkItemState Instance = new(); }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error in stateless worker message loop."
    )]
    private static partial void LogErrorInMessageLoop(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error disposing stateless worker."
    )]
    private static partial void LogErrorInDisposeAsync(ILogger logger, Exception exception);
}
