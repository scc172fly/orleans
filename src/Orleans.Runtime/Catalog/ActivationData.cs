using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Core.Internal;
using Orleans.Diagnostics;
using Orleans.GrainDirectory;
using Orleans.Internal;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Placement;
using Orleans.Runtime.Scheduler;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Runtime;

/// <summary>
/// 维护 Orleans 内部操作所需的每个激活实例的额外状态。
/// 激活状态的并发修改在内部进行同步。
/// </summary>
[DebuggerDisplay("GrainId = {GrainId}, State = {State}, Waiting = {WaitingCount}, Executing = {IsCurrentlyExecuting}")]
internal sealed partial class ActivationData :
    IGrainContext,
    ICollectibleGrainContext,
    IActivationKeepAlive,
    IGrainExtensionBinder,
    IActivationWorkingSetMember,
    IGrainTimerRegistry,
    IGrainManagementExtension,
    IGrainCallCancellationExtension,
    ICallChainReentrantGrainContext,
    IGrainRequestAdmission,
    IAsyncDisposable,
    IDisposable
{
    internal const string GrainAddressMigrationContextKey = "sys.addr";
    private readonly Lock _lock = new();
    private readonly GrainTypeSharedContext _shared;
    private readonly IServiceScope _serviceScope;
    private readonly WorkItemGroup _workItemGroup;
    private readonly List<(Message Message, CoarseStopwatch QueuedTime)> _waitingRequests = new();
    private readonly Dictionary<Message, CoarseStopwatch> _runningRequests = new();
    private readonly SingleWaiterAutoResetEvent _workSignal = new() { RunContinuationsAsynchronously = true };
    private GrainLifecycle? _lifecycle;
    private Queue<object>? _pendingOperations;
    private Message? _blockingRequest;
    private int _runningNonAlwaysInterleaveCount;
    private int _runningNonAlwaysInterleaveWritableCount;

    // 是否存在已开启的排他区段：只有始终允许交错执行的请求以及区段持有者调用链上的
    // 请求可以开始执行。参见 IGrainRequestAdmission.EnterExclusiveSectionAsync。
    private bool _exclusiveSectionOpen;
    private bool _isInWorkingSet = true;
    private CoarseStopwatch _busyDuration;
    private CoarseStopwatch _idleDuration;
    private GrainReference? _selfReference;
    private IActivationCollectionRegistration? _collectionRegistration;

    // 较少使用、出于对象大小考虑不宜直接放在激活实例上的值。
    // 此字段中的值通常表示激活实例的终止状态，或并非
    // 所有 Grain 都使用的功能（例如 Grain 定时器）。
    private ActivationDataExtra? _extras;

    // 表示此激活实例消息循环的任务。
    // 此字段只赋值、不读取，仅供调试使用（例如在内存转储中将循环任务与激活实例关联）。
#pragma warning disable IDE0052 // Remove unread private members
    private Task? _messageLoopTask;
#pragma warning restore IDE0052 // Remove unread private members

    private Activity? _activationActivity;

    /// <summary>
    /// 激活生命周期内使用的 Activity 错误事件名称常量。
    /// </summary>
    private static class ActivityErrorEvents
    {
        public const string InstanceCreateFailed = "instance-create-failed";
        public const string DirectoryRegisterFailed = "directory-register-failed";
        public const string ActivationCancelled = "activation-cancelled";
        public const string ActivationFailed = "activation-failed";
        public const string ActivationError = "activation-error";
        public const string OnActivateFailed = "on-activate-failed";
        public const string OnDeactivateFailed = "on-deactivate-failed";
        public const string RehydrateError = "rehydrate-error";
        public const string DehydrateError = "dehydrate-error";
    }

    public ActivationData(
        GrainAddress grainAddress,
        Func<IGrainContext, WorkItemGroup> createWorkItemGroup,
        IServiceProvider applicationServices,
        GrainTypeSharedContext shared)
    {
        ArgumentNullException.ThrowIfNull(grainAddress);
        ArgumentNullException.ThrowIfNull(createWorkItemGroup);
        ArgumentNullException.ThrowIfNull(applicationServices);
        ArgumentNullException.ThrowIfNull(shared);
        _shared = shared;
        Address = grainAddress;
        _serviceScope = applicationServices.CreateScope();
        Debug.Assert(_serviceScope != null, "_serviceScope must not be null.");
        _workItemGroup = createWorkItemGroup(this);
        Debug.Assert(_workItemGroup != null, "_workItemGroup must not be null.");
    }

    internal void SetActivationActivity(Activity activity)
    {
        _activationActivity = activity;
    }

    /// <summary>
    /// 获取激活 Activity 的上下文（如果存在）。
    /// 这使子 Activity 在激活实例生命周期操作期间能够建立正确的父子关系。
    /// </summary>
    internal ActivityContext? GetActivationActivityContext()
    {
        return _activationActivity?.Context;
    }

    public void Start(IGrainActivator grainActivator)
    {
        Debug.Assert(Equals(ActivationTaskScheduler, TaskScheduler.Current));
        lock (_lock)
        {
            try
            {
                var instance = grainActivator.CreateInstance(this);
                SetGrainInstance(instance);
                _activationActivity?.AddEvent(new ActivityEvent("instance-created"));

                GrainLifecycleEvents.EmitCreated(this);
            }
            catch (Exception exception)
            {
                SetActivityError(_activationActivity, exception, ActivityErrorEvents.InstanceCreateFailed);

                Deactivate(new(DeactivationReasonCode.ActivationFailed, exception, "Error constructing grain instance."), _activationActivity?.Context, CancellationToken.None);
            }

            _messageLoopTask = RunMessageLoop();
        }
    }

    public ActivationTaskScheduler ActivationTaskScheduler => _workItemGroup.TaskScheduler;
    public IGrainRuntime GrainRuntime => _shared.Runtime;
    public object? GrainInstance { get; private set; }
    public GrainAddress Address { get; private set; }
    public GrainReference GrainReference => _selfReference ??= _shared.GrainReferenceActivator.CreateReference(GrainId, default);
    public ActivationState State { get; private set; } = ActivationState.Creating;
    public PlacementStrategy PlacementStrategy => _shared.PlacementStrategy;

    public IServiceProvider ActivationServices => _serviceScope.ServiceProvider;
    public ActivationId ActivationId => Address.ActivationId;
    public IGrainLifecycle ObservableLifecycle
    {
        get
        {
            if (_lifecycle is { } lifecycle) return lifecycle;
            lock (_lock) { return _lifecycle ??= new GrainLifecycle(_shared.Logger); }
        }
    }

    internal GrainTypeSharedContext Shared => _shared;

    public GrainId GrainId => Address.GrainId;
    public bool IsExemptFromCollection => _shared.CollectionAgeLimit == Timeout.InfiniteTimeSpan;
    IActivationCollectionRegistration? ICollectibleGrainContext.CollectionRegistration => Volatile.Read(ref _collectionRegistration);
    private DateTime KeepAliveUntil { get; set; } = DateTime.MinValue;
    public bool IsValid => State is ActivationState.Valid;

    IActivationCollectionRegistration ICollectibleGrainContext.GetOrSetCollectionRegistration(IActivationCollectionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return Interlocked.CompareExchange(ref _collectionRegistration, registration, null) ?? registration;
    }

    // 目前，仅支持使用 StatelessWorkerPlacement 策略的 Grain 拥有多个激活实例。
    internal bool IsStatelessWorker => PlacementStrategy is StatelessWorkerPlacement;

    /// <summary>
    /// 返回一个值，指示此放置策略是否要求将激活实例注册到
    /// Grain 目录中。
    /// </summary>
    internal bool IsUsingGrainDirectory => PlacementStrategy.IsUsingGrainDirectory;

    public int WaitingCount
    {
        get
        {
            lock (_lock)
            {
                return _waitingRequests.Count;
            }
        }
    }

    public bool IsInactive => GetRequestStatus().IsInactive;

    public bool IsCurrentlyExecuting
    {
        get
        {
            lock (_lock)
            {
                return _runningRequests.Count > 0;
            }
        }
    }

    internal (int WaitingCount, bool IsInactive) GetRequestStatus()
    {
        lock (_lock)
        {
            var waitingCount = _waitingRequests.Count;

            // 已开启的排他区段执行诸如事务之类的代码，这些代码不一定是请求。
            return (waitingCount, waitingCount == 0 && _runningRequests.Count == 0 && !_exclusiveSectionOpen);
        }
    }

    internal ValueTask WaitForActivationReadyAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (State is ActivationState.Valid or ActivationState.Invalid)
            {
                return ValueTask.CompletedTask;
            }

            _extras ??= new();
            var completion = _extras.ActivationReady ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask(completion.Task.WaitAsync(cancellationToken));
        }
    }

    public IWorkItemScheduler Scheduler => _workItemGroup;
    public Task Deactivated => GetDeactivationCompletionSource().Task;

    public SiloAddress? ForwardingAddress
    {
        get => _extras?.ForwardingAddress;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.ForwardingAddress = value;
            }
        }
    }

    /// <summary>
    /// 获取此 Grain 先前的目录注册记录（如果已知）。
    /// 在激活期间，用它将 Grain 目录更新为指向新的注册记录。
    /// </summary>
    public GrainAddress? PreviousRegistration
    {
        get => _extras?.PreviousRegistration;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.PreviousRegistration = value;
            }
        }
    }

    private Exception? DeactivationException => _extras?.DeactivationReason.Exception;

    private DeactivationReason DeactivationReason
    {
        get => _extras?.DeactivationReason ?? default;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.DeactivationReason = value;
            }
        }
    }

    private HashSet<IGrainTimer>? Timers
    {
        get => _extras?.Timers;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.Timers = value;
            }
        }
    }

    private DateTime? DeactivationStartTime
    {
        get => _extras?.DeactivationStartTime;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.DeactivationStartTime = value;
            }
        }
    }

    private bool IsStuckDeactivating
    {
        get => _extras?.IsStuckDeactivating ?? false;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.IsStuckDeactivating = value;
            }
        }
    }

    private bool IsStuckProcessingMessage
    {
        get => _extras?.IsStuckProcessingMessage ?? false;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.IsStuckProcessingMessage = value;
            }
        }
    }

    private DehydrationContextHolder? DehydrationContext
    {
        get => _extras?.DehydrationContext;
        set
        {
            lock (_lock)
            {
                _extras ??= new();
                _extras.DehydrationContext = value;
            }
        }
    }

    public TimeSpan CollectionAgeLimit => _shared.CollectionAgeLimit;

    public object? GetTarget() => GrainInstance;

    object? ITargetHolder.GetComponent(Type componentType)
    {
        var result = GetComponent(componentType);
        if (result is null && typeof(IGrainExtension).IsAssignableFrom(componentType))
        {
            var implementation = ActivationServices.GetKeyedService<IGrainExtension>(componentType);
            if (implementation is not { } typedResult)
            {
                throw new GrainExtensionNotInstalledException($"No extension of type {componentType} is installed on this instance and no implementations are registered for automated install");
            }

            SetComponent(componentType, typedResult);
            result = typedResult;
        }

        return result;
    }

    public TComponent? GetComponent<TComponent>() where TComponent : class => GetComponent(typeof(TComponent)) as TComponent;

    public object? GetComponent(Type componentType)
    {
        object? result;
        if (componentType.IsAssignableFrom(GrainInstance?.GetType()))
        {
            result = GrainInstance;
        }
        else if (componentType.IsAssignableFrom(GetType()))
        {
            result = this;
        }
        else if (_extras is { } components && components.TryGetValue(componentType, out var resultObj))
        {
            result = resultObj;
        }
        else if (_shared.GetComponent(componentType) is { } sharedComponent)
        {
            result = sharedComponent;
        }
        else if (ActivationServices.GetService(componentType) is { } component)
        {
            SetComponent(componentType, component);
            result = component;
        }
        else
        {
            result = null;
        }

        return result;
    }

    public void SetComponent<TComponent>(TComponent? instance) where TComponent : class => SetComponent(typeof(TComponent), instance);

    public void SetComponent(Type componentType, object? instance)
    {
        if (componentType.IsAssignableFrom(GrainInstance?.GetType()))
        {
            throw new ArgumentException("Cannot override a component which is implemented by this grain");
        }
        else if (componentType.IsAssignableFrom(GetType()))
        {
            throw new ArgumentException("Cannot override a component which is implemented by this grain context");
        }

        lock (_lock)
        {
            if (instance == null)
            {
                _extras?.Remove(componentType);
                return;
            }

            _extras ??= new();
            _extras[componentType] = instance;
        }
    }

    internal void SetGrainInstance(object grainInstance)
    {
        ArgumentNullException.ThrowIfNull(grainInstance);

        lock (_lock)
        {
            if (GrainInstance is not null)
            {
                throw new InvalidOperationException("Grain instance is already set.");
            }

            if (State is not ActivationState.Creating)
            {
                throw new InvalidOperationException("Grain instance can only be set during creation.");
            }

            GrainInstance = grainInstance;

            _shared.OnCreateActivation(this);
            GetComponent<IActivationLifecycleObserver>()?.OnCreateActivation(this);

            _shared.SetupActivation(this);

            if (grainInstance is ILifecycleParticipant<IGrainLifecycle> participant)
            {
                participant.Participate(ObservableLifecycle);
            }
        }
    }

    private void SetState(ActivationState state)
    {
        Debug.Assert(_lock.IsHeldByCurrentThread);
        State = state;
        if (state is ActivationState.Valid or ActivationState.Invalid)
        {
            var activationReady = _extras?.ActivationReady;
            if (_extras is not null)
            {
                _extras.ActivationReady = null;
            }

            activationReady?.TrySetResult();
        }
    }

    /// <summary>
    /// 检查此激活实例是否过载。
    /// 如果过载，返回 LimitExceededException；否则返回 <c>null</c>。
    /// </summary>
    /// <returns>如果过载，返回 LimitExceededException；否则返回 <c>null</c>。</returns>
    public LimitExceededException? CheckOverloaded()
    {
        string limitName = nameof(SiloMessagingOptions.MaxEnqueuedRequestsHardLimit);
        int maxRequestsHardLimit = _shared.MessagingOptions.MaxEnqueuedRequestsHardLimit;
        int maxRequestsSoftLimit = _shared.MessagingOptions.MaxEnqueuedRequestsSoftLimit;
        if (IsStatelessWorker)
        {
            limitName = nameof(SiloMessagingOptions.MaxEnqueuedRequestsHardLimit_StatelessWorker);
            maxRequestsHardLimit = _shared.MessagingOptions.MaxEnqueuedRequestsHardLimit_StatelessWorker;
            maxRequestsSoftLimit = _shared.MessagingOptions.MaxEnqueuedRequestsSoftLimit_StatelessWorker;
        }

        if (maxRequestsHardLimit <= 0 && maxRequestsSoftLimit <= 0) return null; // 未设置任何限制。

        int count = GetRequestCount();

        if (maxRequestsHardLimit > 0 && count > maxRequestsHardLimit) // 硬性限制。
        {
            LogRejectActivationTooManyRequests(_shared.Logger, count, this, maxRequestsHardLimit);
            return new LimitExceededException(limitName, count, maxRequestsHardLimit, ToString());
        }

        if (maxRequestsSoftLimit > 0 && count > maxRequestsSoftLimit) // 软性限制。
        {
            LogWarnActivationTooManyRequests(_shared.Logger, count, this, maxRequestsSoftLimit);
            return null;
        }

        return null;
    }

    internal int GetRequestCount()
    {
        lock (_lock)
        {
            return _runningRequests.Count + _waitingRequests.Count;
        }
    }

    /// <summary>
    /// 返回此激活实例已空闲的时长。
    /// </summary>
    public TimeSpan GetIdleness() => _idleDuration.Elapsed;

    /// <summary>
    /// 返回此激活实例的空闲时间是否已足以进行回收。
    /// </summary>
    public bool IsStale() => GetIdleness() >= _shared.CollectionAgeLimit;

    public void DelayDeactivation(TimeSpan timespan)
    {
        var rescheduleCollection = false;
        lock (_lock)
        {
            if (timespan == TimeSpan.MaxValue || timespan == Timeout.InfiniteTimeSpan)
            {
                // 将这些值加到当前时间会导致溢出，因此直接使用 DateTime.MaxValue。
                KeepAliveUntil = DateTime.MaxValue;
            }
            else if (timespan <= TimeSpan.Zero)
            {
                // 取消之前的 DelayDeactivation 设置，恢复正常回收行为。
                // 如果先前有生效的保活设置，则重新安排回收，使 Grain 可以在
                // CollectionAgeLimit 后被回收，而无需等到原先安排的遥远将来时间。
                rescheduleCollection = KeepAliveUntil > GrainRuntime.TimeProvider.GetUtcNow().UtcDateTime;
                KeepAliveUntil = DateTime.MinValue;
            }
            else
            {
                KeepAliveUntil = GrainRuntime.TimeProvider.GetUtcNow().UtcDateTime + timespan;
            }
        }

        if (rescheduleCollection)
        {
            _shared.InternalRuntime.ActivationCollector.TryRescheduleCollection(this);
        }
    }

    void IActivationKeepAlive.AcquireKeepAlive()
    {
        lock (_lock)
        {
            _extras ??= new();
            ++_extras.KeepAliveHolds;
        }
    }

    void IActivationKeepAlive.ReleaseKeepAlive()
    {
        lock (_lock)
        {
            Debug.Assert(_extras is { KeepAliveHolds: > 0 });

            // 正在停用或已释放的激活实例不得重新进入工作集。
            if (--_extras!.KeepAliveHolds == 0 && State is ActivationState.Valid)
            {
                MarkActive();
            }
        }
    }

    public void ResetKeepAliveRequest()
    {
        lock (_lock)
        {
            KeepAliveUntil = DateTime.MinValue;
        }
    }

    ActivationCollectionResult ICollectibleGrainContext.TryDeactivateForCollection(
        DeactivationReason reason,
        DateTime now,
        TimeSpan ageLimit,
        bool respectKeepAlive,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (State is not ActivationState.Valid)
            {
                return ActivationCollectionResult.Remove;
            }

            if (respectKeepAlive && KeepAliveUntil > now)
            {
                var keepAliveDuration = KeepAliveUntil - now;
                return ActivationCollectionResult.Reschedule(
                    TimeSpan.FromTicks(Math.Max(keepAliveDuration.Ticks, CollectionAgeLimit.Ticks)));
            }

            if (_waitingRequests.Count > 0
                || _runningRequests.Count > 0
                || _idleDuration.Elapsed < ageLimit
                || respectKeepAlive && _extras is { KeepAliveHolds: > 0 })
            {
                return ActivationCollectionResult.Reschedule(CollectionAgeLimit);
            }

            Deactivate(reason, cancellationToken);
            return ActivationCollectionResult.StartedDeactivation;
        }
    }

    private void ScheduleOperation(object operation)
    {
        lock (_lock)
        {
            _pendingOperations ??= new();
            _pendingOperations.Enqueue(operation);
        }

        _workSignal.Signal();
    }

    private void CancelPendingOperations()
    {
        lock (_lock)
        {
            // 如果 Grain 当前正在激活，则取消该操作。
            if (_pendingOperations is not { Count: > 0 } operations)
            {
                return;
            }

            var opCount = operations.Count;
            // 此处可以使用 ArrayPool<Command>，但必须筛除
            // 非 Command 类型，这意味着需要遍历 _pendingOperations，
            // 从而违背获取快照的初衷。
            // 注意：CopyTo 不使用队列的枚举器，因此可以安全地以此方式获取快照。
            var array = ArrayPool<object>.Shared.Rent(opCount);

            operations.CopyTo(array, 0);

            var snapshot = new Span<object>(array, 0, opCount);

            try
            {
                foreach (var op in snapshot)
                {
                    if (op is Command cmd)
                    {
                        try
                        {
                            cmd.Cancel();
                        }
                        catch (Exception exception)
                        {
                            if (exception is not ObjectDisposedException)
                            {
                                LogErrorCancellingOperation(_shared.Logger, exception, cmd);
                            }
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<object>.Shared.Return(array, true);
            }
        }
    }

    public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) =>
        TryStartMigration(requestContext, cancellationToken);

    internal bool TryStartMigration(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (State is not (ActivationState.Activating or ActivationState.Valid or ActivationState.Deactivating))
            {
                return false;
            }

            // 如果迁移尚未开始，则设置迁移上下文，以捕获需要传输的状态。
            // 这将通知停用流程正在发生迁移，因此必须在开始停用之前执行。
            DehydrationContext ??= new(_shared.SerializerSessionPool, requestContext);

            if (State is not ActivationState.Deactivating)
            {
                // 开始停用 Grain，为迁移做准备。
                Deactivate(new DeactivationReason(DeactivationReasonCode.Migrating, "Migrating to a new location."), cancellationToken);
            }

            return true;
        }
    }
    public void Deactivate(DeactivationReason reason, ActivityContext? activityContext, CancellationToken cancellationToken = default)
    {
        var currentActivity = Activity.Current;
        var deactivateActivity = activityContext is { } parent
            ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.DeactivateGrain, ActivityKind.Internal, parentContext: parent)
            : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.DeactivateGrain);
        lock (_lock)
        {
            try
            {
                var state = State;
                if (deactivateActivity is { IsAllDataRequested: true })
                {
                    deactivateActivity.SetTag(ActivityTagKeys.GrainState, state);
                }

                if (state is ActivationState.Invalid)
                {
                    deactivateActivity?.Stop();
                    return;
                }

                if (DeactivationReason.ReasonCode == DeactivationReasonCode.None)
                {
                    DeactivationReason = reason;
                }

                if (deactivateActivity is { IsAllDataRequested: true })
                {
                    deactivateActivity.SetTag(ActivityTagKeys.DeactivationReason, DeactivationReason);
                }

                if (!DeactivationStartTime.HasValue)
                {
                    DeactivationStartTime = GrainRuntime.TimeProvider.GetUtcNow().UtcDateTime;
                }

                if (state is ActivationState.Creating or ActivationState.Activating or ActivationState.Valid)
                {
                    GrainLifecycleEvents.EmitDeactivating(this, DeactivationReason);

                    CancelPendingOperations();

                    _shared.InternalRuntime.ActivationWorkingSet.OnDeactivating(this);
                    SetState(ActivationState.Deactivating);
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_shared.InternalRuntime.CollectionOptions.Value.DeactivationTimeout);
                    ScheduleOperation(new Command.Deactivate(cts, state, deactivateActivity));
                }
                else
                {
                    deactivateActivity?.Stop();
                }

                Debug.Assert(State is ActivationState.Deactivating or ActivationState.Invalid, "Deactivate should leave the activation deactivating or invalid.");
            }
            catch (Exception ex)
            {
                SetActivityError(deactivateActivity, ex, "Error deactivating grain");
                deactivateActivity?.Stop();
                throw;
            }
            finally
            {
                Activity.Current = currentActivity;
            }
        }
    }

    public void Deactivate(DeactivationReason reason, CancellationToken cancellationToken = default) => Deactivate(reason, Activity.Current?.Context, cancellationToken);

    private void DeactivateStuckActivation()
    {
        IsStuckProcessingMessage = true;
        var msg = $"Activation {this} has been processing request {_blockingRequest} since {_busyDuration} and is likely stuck.";
        var reason = new DeactivationReason(DeactivationReasonCode.ActivationUnresponsive, msg);

        // 将 Grain 标记为正在停用，以便转发消息而非调用处理程序。
        Deactivate(reason, cancellationToken: default);

        // 尝试从激活实例目录和 Grain 目录中移除此激活实例。
        // 这会使此激活实例处于悬置状态，继续卡在当前请求的处理上，直到请求最终完成
        // （此时很可能永远无法完成，因为 Grain 被判定为卡住通常意味着存在某种
        // 应用程序缺陷，例如死锁）。
        UnregisterMessageTarget();
        _shared.InternalRuntime.GrainLocator.Unregister(Address, UnregistrationCause.Force).Ignore();
    }

    void IGrainTimerRegistry.OnTimerCreated(IGrainTimer timer)
    {
        lock (_lock)
        {
            Timers ??= new HashSet<IGrainTimer>();
            Timers.Add(timer);
        }
    }

    void IGrainTimerRegistry.OnTimerDisposed(IGrainTimer timer)
    {
        lock (_lock) // 必须加锁，因为 dispose 可能在 Grain 上下文之外的终结器线程上调用（并非单线程）。
        {
            if (Timers is null)
            {
                return;
            }

            Timers.Remove(timer);
        }
    }

    private void DisposeTimers()
    {
        lock (_lock)
        {
            if (Timers is null)
            {
                return;
            }

            // 必须将 Timers 设为 null，因为非 null 时 OnTimerDisposed 会修改定时器集合。
            var timers = Timers;
            Timers = null;

            // 释放所有定时器。
            foreach (var timer in timers)
            {
                timer.Dispose();
            }
        }
    }

    public void AnalyzeWorkload(DateTime now, IMessageCenter messageCenter, MessageFactory messageFactory, SiloMessagingOptions options)
    {
        var slowRunningRequestDuration = options.RequestProcessingWarningTime;
        var longQueueTimeDuration = options.RequestQueueDelayWarningTime;

        List<string>? diagnostics = null;
        lock (_lock)
        {
            if (State != ActivationState.Valid)
            {
                return;
            }

            if (_blockingRequest is not null)
            {
                var message = _blockingRequest;
                TimeSpan? timeSinceQueued = default;
                if (_runningRequests.TryGetValue(message, out var waitTime))
                {
                    timeSinceQueued = waitTime.Elapsed;
                }

                var executionTime = _busyDuration.Elapsed;
                if (executionTime >= slowRunningRequestDuration && !message.IsLocalOnly)
                {
                    GetStatusList(ref diagnostics);
                    if (timeSinceQueued.HasValue)
                    {
                        diagnostics.Add($"Message {message} was enqueued {timeSinceQueued} ago and has now been executing for {executionTime}.");
                    }
                    else
                    {
                        diagnostics.Add($"Message {message} has been executing for {executionTime}.");
                    }

                    var response = messageFactory.CreateDiagnosticResponseMessage(message, isExecuting: true, isWaiting: false, diagnostics);
                    messageCenter.SendMessage(response);
                }
            }

            foreach (var running in _runningRequests)
            {
                var message = running.Key;
                var runDuration = running.Value;
                if (ReferenceEquals(message, _blockingRequest) || message.IsLocalOnly)
                {
                    continue;
                }

                // 检查它们已经执行了多长时间。
                var executionTime = runDuration.Elapsed;
                if (executionTime >= slowRunningRequestDuration)
                {
                    // 可交错执行的消息 X 已执行了很长时间。
                    GetStatusList(ref diagnostics);
                    var messageDiagnostics = new List<string>(diagnostics)
                    {
                        $"Interleaving message {message} has been executing for {executionTime}."
                    };

                    var response = messageFactory.CreateDiagnosticResponseMessage(message, isExecuting: true, isWaiting: false, messageDiagnostics);
                    messageCenter.SendMessage(response);
                }
            }

            var queueLength = 1;
            foreach (var pair in _waitingRequests)
            {
                var message = pair.Message;
                if (message.IsLocalOnly)
                {
                    continue;
                }

                var queuedTime = pair.QueuedTime.Elapsed;
                if (queuedTime >= longQueueTimeDuration)
                {
                    // 消息 X 已在目标 Grain 上排队 Y 时间，目前在待处理队列中的位置为 QueueLength。
                    GetStatusList(ref diagnostics);
                    var messageDiagnostics = new List<string>(diagnostics)
                    {
                       $"Message {message} has been enqueued on the target grain for {queuedTime} and is currently position {queueLength} in queue for processing."
                    };

                    var response = messageFactory.CreateDiagnosticResponseMessage(message, isExecuting: false, isWaiting: true, messageDiagnostics);
                    messageCenter.SendMessage(response);
                }

                queueLength++;
            }
        }

        void GetStatusList([NotNull] ref List<string>? diagnostics)
        {
            if (diagnostics is not null) return;

            diagnostics = new List<string>
            {
                ToDetailedString(),
                $"TaskScheduler status: {_workItemGroup.DumpStatus()}"
            };
        }
    }

    public override string ToString() => $"[Activation: {Address.SiloAddress}/{GrainId}{ActivationId}{GetActivationInfoString()} State={State}]";

    internal string ToDetailedString(bool includeExtraDetails = false)
    {
        lock (_lock)
        {
            var currentlyExecuting = includeExtraDetails ? _blockingRequest : null;
            return @$"[Activation: {Address.SiloAddress}/{GrainId}{ActivationId} {GetActivationInfoString()} State={State} NonReentrancyQueueSize={WaitingCount} NumRunning={_runningRequests.Count} IdlenessTimeSpan={GetIdleness()} CollectionAgeLimit={_shared.CollectionAgeLimit}{(currentlyExecuting != null ? " CurrentlyExecuting=" : null)}{currentlyExecuting}]";
        }
    }

    private string GetActivationInfoString()
    {
        var placement = PlacementStrategy?.GetType().Name;
        var grainTypeName = TryGetGrainTypeName();
        return grainTypeName is null ? $"#Placement={placement}" : $"#GrainType={grainTypeName} Placement={placement}";
    }

    private string? TryGetGrainTypeName()
    {
        return _shared.GrainTypeName ?? GrainInstance switch
        {
            { } grainInstance => RuntimeTypeNameFormatter.Format(grainInstance.GetType()),
            _ => null
        };
    }

    public void Dispose()
    {
#pragma warning disable RS0030 // IDisposable 要求同步完成激活实例清理。
        DisposeAsync().AsTask().Wait();
#pragma warning restore RS0030
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _extras ??= new();
            if (_extras.IsDisposing) return;
            _extras.IsDisposing = true;
        }

        CancelPendingOperations();

        lock (_lock)
        {
            _shared.InternalRuntime.ActivationWorkingSet.OnDeactivated(this);
            SetState(ActivationState.Invalid);
        }

        DisposeTimers();

        try
        {
            try
            {
                var activator = _shared.GetComponent(typeof(IGrainActivator)) as IGrainActivator;
                if (activator != null && GrainInstance is { } instance)
                {
                    await activator.DisposeInstance(this, instance);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }
        finally
        {
            try
            {
                if (GrainInstance is not null)
                {
                    _shared.OnDestroyActivation(this);
                }

                GetComponent<IActivationLifecycleObserver>()?.OnDestroyActivation(this);
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                await DisposeAsync(_serviceScope);
            }
        }
    }

    private static async ValueTask DisposeAsync(object obj)
    {
        try
        {
            if (obj is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (obj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // 忽略。
        }
    }

    bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);

    public (TExtension, TExtensionInterface) GetOrSetExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
        where TExtension : class, TExtensionInterface
        where TExtensionInterface : class, IGrainExtension
    {
        TExtension implementation;
        if (GetComponent<TExtensionInterface>() is object existing)
        {
            if (existing is TExtension typedResult)
            {
                implementation = typedResult;
            }
            else
            {
                throw new InvalidCastException($"Cannot cast existing extension of type {existing.GetType()} to target type {typeof(TExtension)}");
            }
        }
        else
        {
            implementation = newExtensionFunc();
            SetComponent<TExtensionInterface>(implementation);
        }

        var reference = GrainReference.Cast<TExtensionInterface>();
        return (implementation, reference);
    }

    public TExtensionInterface GetExtension<TExtensionInterface>()
        where TExtensionInterface : class, IGrainExtension
    {
        if (GetComponent<TExtensionInterface>() is TExtensionInterface result)
        {
            return result;
        }

        var implementation = ActivationServices.GetKeyedService<IGrainExtension>(typeof(TExtensionInterface));
        if (implementation is not TExtensionInterface typedResult)
        {
            throw new GrainExtensionNotInstalledException($"No extension of type {typeof(TExtensionInterface)} is installed on this instance and no implementations are registered for automated install");
        }

        SetComponent(typedResult);
        return typedResult;
    }

    bool IActivationWorkingSetMember.IsCandidateForRemoval(bool wouldRemove)
    {
        const int IdlenessLowerBound = 10_000;
        lock (_lock)
        {
            var inactive = IsInactive && _idleDuration.ElapsedMilliseconds > IdlenessLowerBound && _extras is not { KeepAliveHolds: > 0 };

            // 如果此实例未等待移除，或当前处于活跃状态，则仍保留在工作集中。
            _isInWorkingSet = !wouldRemove || !inactive;
            return inactive;
        }
    }

    private async Task RunMessageLoop()
    {
        // 注意，此循环永不终止。看起来可能很奇怪，但原因如下：
        // Grain 必须始终接受并处理任何传入消息。如何处理
        // 这些消息由 Grain 的状态决定。如果 Grain 尚未
        // 完成激活，消息将持续排队，直到激活完成。
        // 如果 Grain 激活失败，将以拒绝响应消息。
        // 如果 Grain 已终止，消息将转发给该 Grain 的新实例。
        // Grain 停用且不再有任何 GC 根可达的引用后，此循环最终将由
        // 垃圾回收器回收。
        while (true)
        {
            try
            {
                if (!IsCurrentlyExecuting)
                {
                    bool hasPendingOperations;
                    lock (_lock)
                    {
                        hasPendingOperations = _pendingOperations is { Count: > 0 };
                    }

                    if (hasPendingOperations)
                    {
                        await ProcessOperationsAsync();
                    }
                }

                ProcessPendingRequests();

                await _workSignal.WaitAsync();
            }
            catch (Exception exception)
            {
                LogErrorInGrainMessageLoop(_shared.Logger, exception);
            }
        }

        void ProcessPendingRequests()
        {
            // 此激活实例不会处理的请求在持锁时从队列中移除，待释放锁后再拒绝或转发，
            // 因为这可能向其他激活实例投递消息、完成本地请求，进而
            // 在此激活实例上排入更多请求。
            List<Message>? unprocessableRequests = null;
            var i = 0;

            do
            {
                Message? message = null;
                Exception? rejection = null;
                lock (_lock)
                {
                    if (_waitingRequests.Count <= i)
                    {
                        break;
                    }

                    message = _waitingRequests[i].Message;

                    // 如果激活实例无效，则拒绝所有待处理消息，唯独仅限本地的消息除外。
                    // 仅限本地的消息用于内部系统操作，Grain 有效或正在停用时不应拒绝它们。
                    if (State != ActivationState.Valid && !(message.IsLocalOnly && State is ActivationState.Deactivating))
                    {
                        unprocessableRequests = DequeueUnprocessableRequests();
                        break;
                    }

                    try
                    {
                        if (!MayInvokeRequest(message))
                        {
                            // 激活实例当前无法处理此消息，因此尝试下一条消息。
                            ++i;

                            if (_blockingRequest != null)
                            {
                                var currentRequestActiveTime = _busyDuration.Elapsed;
                                if (currentRequestActiveTime > _shared.MaxRequestProcessingTime && !IsStuckProcessingMessage)
                                {
                                    DeactivateStuckActivation();
                                }
                                else if (currentRequestActiveTime > _shared.MaxWarningRequestProcessingTime)
                                {
                                    // 考虑：为可重入激活实例处理长请求检测；此逻辑只适用于不可重入激活实例。
                                    LogWarningDispatcher_ExtendedMessageProcessing(
                                        _shared.Logger,
                                        currentRequestActiveTime,
                                        new(this),
                                        _blockingRequest,
                                        message);
                                }
                            }

                            continue;
                        }

                        // 如果当前消息不兼容，则停用此激活实例，最终将消息转发给新的激活实例。
                        if (message.InterfaceVersion > 0)
                        {
                            var compatibilityDirector = _shared.InternalRuntime.CompatibilityDirectorManager.GetDirector(message.InterfaceType);
                            var currentVersion = _shared.InternalRuntime.GrainVersionManifest.GetLocalVersion(message.InterfaceType);
                            if (!compatibilityDirector.IsCompatible(message.InterfaceVersion, currentVersion))
                            {
                                // 将此激活实例添加到缓存失效标头中。
                                message.AddToCacheInvalidationHeader(Address, validAddress: null);

                                var reason = new DeactivationReason(
                                    DeactivationReasonCode.IncompatibleRequest,
                                    $"Received incompatible request for interface {message.InterfaceType} version {message.InterfaceVersion}. This activation supports interface version {currentVersion}.");

                                var activityContext = message.RequestContextData.TryGetActivityContext();

                                Deactivate(reason, activityContext, cancellationToken: default);
                                return;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        rejection = exception;
                    }

                    // 将消息移出队列，以便处理或拒绝它。
                    _waitingRequests.RemoveAt(i);
                    if (rejection is null)
                    {
                        Debug.Assert(State == ActivationState.Valid || message.IsLocalOnly);
                        RecordRunning(message, message.IsAlwaysInterleave);
                    }
                }

                if (rejection is not null)
                {
                    RejectRequest(message, rejection);
                    continue;
                }

                // 在锁外开始调用消息处理程序。
                InvokeIncomingRequest(message);
            }
            while (true);

            if (unprocessableRequests is not null)
            {
                RejectOrForwardRequests(unprocessableRequests);
            }
        }

        void RecordRunning(Message message, bool isAlwaysInterleave)
        {
            var stopwatch = CoarseStopwatch.StartNew();
            _runningRequests.Add(message, stopwatch);

            if (isAlwaysInterleave) return;

            ++_runningNonAlwaysInterleaveCount;
            if (!message.IsReadOnly)
            {
                ++_runningNonAlwaysInterleaveWritableCount;
            }

            if (_blockingRequest != null) return;

            // 此逻辑仅适用于不可重入激活实例。
            // 考虑：为可重入激活实例处理长请求检测。
            _blockingRequest = message;
            _busyDuration = stopwatch;
        }

        // 在持有锁时调用。当此激活实例无法再处理等待中的请求时将其出队，
        // 若仍可能处理这些请求，则返回 null。
        List<Message>? DequeueUnprocessableRequests()
        {
            if (State is ActivationState.Creating or ActivationState.Activating)
            {
                // 在激活实例成为有效或无效状态之前不执行任何操作。
                return null;
            }

            if (State is ActivationState.Deactivating)
            {
                // 决定是否将此激活实例宣告为卡住。
                var deactivatingTime = GrainRuntime.TimeProvider.GetUtcNow().UtcDateTime - DeactivationStartTime!.Value;
                if (deactivatingTime > _shared.MaxRequestProcessingTime && !IsStuckDeactivating)
                {
                    IsStuckDeactivating = true;
                    if (DeactivationReason.Description is { Length: > 0 } && DeactivationReason.ReasonCode != DeactivationReasonCode.ActivationUnresponsive)
                    {
                        DeactivationReason = new(DeactivationReasonCode.ActivationUnresponsive,
                            $"{DeactivationReason.Description}. Activation {this} has been deactivating since {DeactivationStartTime.Value} and is likely stuck");
                    }

                    AbandonStuckDeactivatingActivation();
                }

                if (!IsStuckDeactivating && !IsStuckProcessingMessage)
                {
                    // Grain 仍在停用且尚未被判定为卡住时，不要转发消息，因为这些消息
                    // 会被转发回同一 Grain 实例。
                    return null;
                }
            }

            return TakeWaitingRequests();
        }

        void AbandonStuckDeactivatingActivation()
        {
            var forwardingAddress = ForwardingAddress;
            LogWarningAbandoningStuckDeactivatingActivation(_shared.Logger, this, forwardingAddress);

            // 直到停用流程到达 StartMigrationAsync，迁移目标才得到确认。
            // 如果在此之前停用流程卡住，应重新解析消息地址，而不是将消息转发到
            // 可能尚无有效替代激活实例的目标。
            ForwardingAddress = null;
            UnregisterMessageTarget();
            _shared.InternalRuntime.GrainLocator.Unregister(Address, UnregistrationCause.Force).Ignore();
            GetDeactivationCompletionSource().TrySetResult(true);
        }

        bool MayInvokeRequest(Message incoming)
        {
            if (_exclusiveSectionOpen)
            {
                // 排他区段开启期间，只有始终允许交错执行的请求和区段持有者调用链上的请求才
                // 能开始执行。
                if (incoming.IsAlwaysInterleave)
                {
                    return true;
                }

                var reentrancyId = incoming.GetReentrancyId();
                return reentrancyId != Guid.Empty && reentrancyId == _extras?.ExclusiveSectionReentrancyId;
            }

            if (!IsCurrentlyExecuting)
            {
                return true;
            }

            // 否则，若 Grain 可重入或消息允许交错执行，则允许调用请求。
            if (incoming.IsAlwaysInterleave)
            {
                return true;
            }

            if (_runningNonAlwaysInterleaveCount == 0
                || (incoming.IsReadOnly && _runningNonAlwaysInterleaveWritableCount == 0))
            {
                return true;
            }

            // 处理调用链可重入性。
            if (incoming.GetReentrancyId() is Guid id
                && IsReentrantSection(id))
            {
                return true;
            }

            var canInterleave = GetComponent<GrainCanInterleave>();
            bool? incomingMayInterleave = null;
            foreach (var runningRequest in _runningRequests)
            {
                var runningMessage = runningRequest.Key;
                if (runningMessage.IsAlwaysInterleave
                    || (runningMessage.IsReadOnly && incoming.IsReadOnly))
                {
                    continue;
                }

                if (canInterleave is not null)
                {
                    try
                    {
                        incomingMayInterleave ??= canInterleave.MayInterleave(GrainInstance, incoming);
                        if (incomingMayInterleave.Value)
                        {
                            return true;
                        }

                        if (canInterleave.MayInterleave(GrainInstance, runningMessage))
                        {
                            continue;
                        }
                    }
                    catch (Exception exception)
                    {
                        LogErrorInvokingMayInterleavePredicate(_shared.Logger, exception, this, incoming);
                        throw;
                    }
                }

                _blockingRequest = runningMessage;
                _busyDuration = runningRequest.Value;
                return false;
            }

            return true;
        }

        async Task ProcessOperationsAsync()
        {
            object? op = null;
            while (true)
            {
                lock (_lock)
                {
                    Debug.Assert(_pendingOperations is not null);

                    // 移除上一个操作。
                    // 操作仅在完成后才被移除，使各操作能够感知彼此。
                    // 例如，停用请求可以看到正在进行的激活请求并取消它。
                    if (op is not null)
                    {
                        _pendingOperations.Dequeue();
                    }

                    // 尝试获取下一个操作。
                    if (!_pendingOperations.TryPeek(out op))
                    {
                        _pendingOperations = null;
                        return;
                    }
                }

                try
                {
                    switch (op)
                    {
                        case Command.Rehydrate command:
                            RehydrateInternal(command.Context);
                            break;
                        case Command.Activate command:
                            await ActivateAsync(command.RequestContext, command.Metrics, command.CancellationToken).SuppressThrowing();
                            break;
                        case Command.Deactivate command:
                            await FinishDeactivating(command, command.CancellationToken).SuppressThrowing();
                            break;
                        case Command.Delay command:
                            await Task.Delay(command.Duration, GrainRuntime.TimeProvider, command.CancellationToken).SuppressThrowing();
                            break;
                        default:
                            throw new NotSupportedException($"Encountered unknown operation of type {op?.GetType().ToString() ?? "null"} {op}.");
                    }
                }
                catch (Exception exception)
                {
                    LogErrorInProcessOperationsAsync(_shared.Logger, exception, this);
                }
                finally
                {
                    if (op is not null)
                    {
                        await DisposeAsync(op);
                    }
                }
            }
        }
    }

    private void RehydrateInternal(IRehydrationContext context)
    {
        Activity? rehydrateSpan = null;
        try
        {
            LogRehydratingGrain(_shared.Logger, this);

            var grainMigrationParticipant = GrainInstance as IGrainMigrationParticipant;

            if (grainMigrationParticipant is not null)
            {
                // 为状态恢复（rehydration）启动一个跟踪跨度。
                rehydrateSpan = _activationActivity is not null
                    ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivationRehydrate,
                        ActivityKind.Internal, _activationActivity.Context)
                    : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivationRehydrate,
                        ActivityKind.Internal);
                rehydrateSpan?.SetTag(ActivityTagKeys.GrainId, GrainId.ToString());
                rehydrateSpan?.SetTag(ActivityTagKeys.GrainType, _shared.GrainTypeName);
                rehydrateSpan?.SetTag(ActivityTagKeys.SiloId, _shared.Runtime.SiloAddress.ToString());
                rehydrateSpan?.SetTag(ActivityTagKeys.ActivationId, ActivationId.ToString());
            }

            lock (_lock)
            {
                if (State != ActivationState.Creating)
                {
                    LogIgnoringRehydrateAttempt(_shared.Logger, this, State);
                    rehydrateSpan?.SetTag(ActivityTagKeys.RehydrateIgnored, true);
                    rehydrateSpan?.SetTag(ActivityTagKeys.RehydrateIgnoredReason, $"State is {State}");
                    return;
                }

                if (context.TryGetValue(GrainAddressMigrationContextKey, out GrainAddress? previousRegistration) &&
                    previousRegistration is not null)
                {
                    if (previousRegistration.GrainId != GrainId)
                    {
                        throw new ArgumentException("The previous registration must identify the grain being rehydrated.", nameof(context));
                    }

                    PreviousRegistration = previousRegistration;
                    LogPreviousActivationAddress(_shared.Logger, previousRegistration);
                    rehydrateSpan?.SetTag(ActivityTagKeys.RehydratePreviousRegistration,
                        previousRegistration.ToFullString());

                    foreach (var (message, _) in _waitingRequests)
                    {
                        message.AddToCacheInvalidationHeader(previousRegistration, Address);
                    }
                }

                if (_lifecycle is { } lifecycle)
                {
                    foreach (var participant in lifecycle.GetMigrationParticipants())
                    {
                        participant.OnRehydrate(context);
                    }
                }

                grainMigrationParticipant?.OnRehydrate(context);
            }

            LogRehydratedGrain(_shared.Logger);
            rehydrateSpan?.AddEvent(new ActivityEvent("rehydrated"));
        }
        catch (Exception exception)
        {
            LogErrorRehydratingActivation(_shared.Logger, exception);
            SetActivityError(rehydrateSpan, exception, ActivityErrorEvents.RehydrateError);
        }
        finally
        {
            rehydrateSpan?.Dispose();
        }
    }

    private void OnDehydrate(IDehydrationContext context)
    {
        LogDehydratingActivation(_shared.Logger);

        lock (_lock)
        {
            Debug.Assert(context is not null);

            if (IsUsingGrainDirectory)
            {
                context.TryAddValue(GrainAddressMigrationContextKey, Address);
            }

            Activity? dehydrateSpan = null;
            try
            {
                // 从状态提取上下文持有者获取父 Activity 上下文（迁移开始时捕获）。
                var parentContext = DehydrationContext?.MigrationActivityContext;

                var grainMigrationParticipant = GrainInstance as IGrainMigrationParticipant;

                if (grainMigrationParticipant is not null)
                {
                    // 为状态提取（dehydration）启动跟踪跨度，将触发它的迁移请求作为父级。
                    dehydrateSpan = parentContext.HasValue
                        ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivationDehydrate,
                            ActivityKind.Internal, parentContext.Value)
                        : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivationDehydrate,
                            ActivityKind.Internal);
                    if (dehydrateSpan is { IsAllDataRequested: true })
                    {
                        dehydrateSpan.SetTag(ActivityTagKeys.GrainId, GrainId.ToString());
                        dehydrateSpan.SetTag(ActivityTagKeys.GrainType, _shared.GrainTypeName);
                        dehydrateSpan.SetTag(ActivityTagKeys.SiloId, _shared.Runtime.SiloAddress.ToString());
                        dehydrateSpan.SetTag(ActivityTagKeys.ActivationId, ActivationId.ToString());
                        if (ForwardingAddress is { } fwd)
                        {
                            dehydrateSpan.SetTag(ActivityTagKeys.MigrationTargetSilo, fwd.ToString());
                        }
                    }
                }

                // 注意，这些调用的顺序与 Rehydrate 相反，没有特别的原因，只是为了对称。
                grainMigrationParticipant?.OnDehydrate(context);

                if (_lifecycle is { } lifecycle)
                {
                    foreach (var participant in lifecycle.GetMigrationParticipants())
                    {
                        participant.OnDehydrate(context);
                    }
                }
            }
            catch (Exception exception)
            {
                LogErrorDehydratingActivation(_shared.Logger, exception);
                SetActivityError(dehydrateSpan, exception, ActivityErrorEvents.DehydrateError);
            }
            finally
            {
                dehydrateSpan?.Dispose();
            }
        }

        LogDehydratedActivation(_shared.Logger);
    }


    /// <summary>
    /// 处理传入消息，将相应处理程序排队或调用它。
    /// </summary>
    /// <param name="message"></param>
    private void InvokeIncomingRequest(Message message)
    {
        _shared.MessagingProcessingInstruments.OnDispatcherMessageProcessedOk(message);

        try
        {
            var task = _shared.InternalRuntime.RuntimeClient.Invoke(this, message);

            // 注意：无论成功还是发生故障，此逻辑都会执行。
            if (task.IsCompleted)
            {
                OnCompletedRequest(message);
            }
            else
            {
                _ = OnCompleteAsync(this, message, task);
            }
        }
        catch
        {
            OnCompletedRequest(message);
        }

        static async Task OnCompleteAsync(ActivationData activation, Message message, Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
            finally
            {
                activation.OnCompletedRequest(message);
            }
        }
    }

    /// <summary>
    /// 当激活实例完成一个事务并可能准备好处理其他事务时调用。
    /// </summary>
    /// <param name="message">刚刚处理完成的消息。</param>
    private void OnCompletedRequest(Message message)
    {
        lock (_lock)
        {
            if (_runningRequests.Remove(message) && !message.IsAlwaysInterleave)
            {
                --_runningNonAlwaysInterleaveCount;
                if (!message.IsReadOnly)
                {
                    --_runningNonAlwaysInterleaveWritableCount;
                }

                Debug.Assert(_runningNonAlwaysInterleaveCount >= 0);
                Debug.Assert(_runningNonAlwaysInterleaveWritableCount >= 0);
            }

            // 如果消息应使激活实例保持活跃，则重置空闲计时器，并确保激活实例
            // 位于激活实例工作集中。
            if (message.IsKeepAlive)
            {
                MarkActive();
            }

            // 下面的逻辑仅适用于不可重入激活实例。
            if (_blockingRequest is null || message.Equals(_blockingRequest))
            {
                _blockingRequest = null;
                _busyDuration = default;
            }
        }

        // 向消息泵发出信号：此请求已经完成，检查是否存在其他现在可以处理的请求。
        _workSignal.Signal();
    }

    // 在此处同步地在调用方的执行流中读取持有者的调用链。
    ValueTask<IDisposable> IGrainRequestAdmission.EnterExclusiveSectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reentrancyId = RequestContext.ReentrancyId;
        ExclusiveSectionWaiter waiter;
        lock (_lock)
        {
            if (!_exclusiveSectionOpen)
            {
                Debug.Assert(_extras?.ExclusiveSectionWaiters is not { Count: > 0 }, "Sections wait only while one is open.");
                Debug.Assert(_extras is not { } extras || extras.ExclusiveSectionReentrancyId == Guid.Empty, "Only an open section has a call chain.");
                _exclusiveSectionOpen = true;
                if (reentrancyId != Guid.Empty)
                {
                    (_extras ??= new()).ExclusiveSectionReentrancyId = reentrancyId;
                }

                return new(new ExclusiveSection(this));
            }

            var waiters = (_extras ??= new()).ExclusiveSectionWaiters ??= new();
            if (waiters.Closed is { } closed)
            {
                return ValueTask.FromException<IDisposable>(closed);
            }

            waiter = new(reentrancyId);
            waiters.Enqueue(waiter);
        }

        return WaitAsync(this, waiter, cancellationToken);

        static async ValueTask<IDisposable> WaitAsync(ActivationData activation, ExclusiveSectionWaiter waiter, CancellationToken cancellationToken)
        {
            // 已取消的等待者仍留在队列中，下次退出区段时将跳过它。
            using (cancellationToken.UnsafeRegister(static (state, token) => ((ExclusiveSectionWaiter)state!).TrySetCanceled(token), waiter))
            {
                await waiter.Task;
            }

            return new ExclusiveSection(activation);
        }
    }

    // 关闭已开启的区段，或将区段连同其持有者的调用链交给下一个等待区段，从而保持区段开启。
    private void ExitExclusiveSection()
    {
        bool signal;
        lock (_lock)
        {
            Debug.Assert(_exclusiveSectionOpen);
            var next = HandOffExclusiveSection();
            if (_extras is { } extras)
            {
                extras.ExclusiveSectionReentrancyId = next?.ReentrancyId ?? Guid.Empty;
            }

            _exclusiveSectionOpen = next is not null;

            // 等待区段的请求，或位于下一个持有者调用链上的请求，现在可以开始执行。
            signal = _waitingRequests.Count > 0 && (next is null || next.ReentrancyId != Guid.Empty);
        }

        if (signal)
        {
            _workSignal.Signal();
        }
    }

    // 返回第一个接受已开启区段的等待区段；若无等待者则返回 null。必须在持有 _lock 时调用。
    private ExclusiveSectionWaiter? HandOffExclusiveSection()
    {
        if (_extras?.ExclusiveSectionWaiters is not { } waiters)
        {
            return null;
        }

        while (waiters.TryDequeue(out var next))
        {
            if (next.TrySetResult())
            {
                return next;
            }
        }

        if (waiters.Closed is null)
        {
            _extras.ExclusiveSectionWaiters = null;
        }

        return null;
    }

    // 激活实例完成停用后，使仍在等待的区段以故障结束：已开启区段中的代码可能永不结束，因为
    // 停用不会等待非请求代码，而且后续区段已无法开启。
    private void CloseExclusiveSections()
    {
        lock (_lock)
        {
            if (!_exclusiveSectionOpen)
            {
                return;
            }

            var waiters = (_extras ??= new()).ExclusiveSectionWaiters ??= new();
            var closed = waiters.Closed ??= new InvalidOperationException($"Activation {this} is deactivating, so it opens no more exclusive sections.");
            while (waiters.TryDequeue(out var waiter))
            {
                waiter.TrySetException(closed);
            }
        }
    }

    // 激活实例上一个已开启的排他区段，对其调用 Dispose 只会关闭一次。
    private sealed class ExclusiveSection(ActivationData activation) : IDisposable
    {
        private ActivationData? _activation = activation;

        public void Dispose()
        {
            var activation = _activation;
            _activation = null;
            activation?.ExitExclusiveSection();
        }
    }

    // 等待已开启区段关闭的区段，携带请求该区段的代码的调用链可重入性 ID。
    private sealed class ExclusiveSectionWaiter(Guid reentrancyId) : TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public Guid ReentrancyId { get; } = reentrancyId;
    }

    // 按请求顺序保存等待已开启区段关闭的区段。
    private sealed class ExclusiveSectionWaiters : Queue<ExclusiveSectionWaiter>
    {
        // 激活实例完成停用后设置；此后需要等待的区段以故障结束。
        public Exception? Closed { get; set; }
    }

    /// <summary>
    /// 重新开始空闲计时，并确保激活实例处于工作集中。必须在持有 <see cref="_lock"/> 时调用。
    /// </summary>
    private void MarkActive()
    {
        _idleDuration = CoarseStopwatch.StartNew();

        if (!_isInWorkingSet)
        {
            _isInWorkingSet = true;
            _shared.InternalRuntime.ActivationWorkingSet.OnActive(this);
        }
    }

    public void ReceiveMessage(object message) => ReceiveMessage((Message)message);
    public void ReceiveMessage(Message message)
    {
        _shared.InternalRuntime.MessagingTrace.OnDispatcherReceiveMessage(message);

        // 不处理已经超时的消息。
        if (message.IsExpired)
        {
            _shared.MessagingProcessingInstruments.OnDispatcherMessageProcessedError(message);
            _shared.InternalRuntime.MessagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Dispatch);
            message.Dispose();
            return;
        }

        if (message.Direction == Message.Directions.Response)
        {
            ReceiveResponse(message);
        }
        else // Request or OneWay
        {
            ReceiveRequest(message);
        }
    }

    public void ReceiveLocalRequest(ILocalRequest request, LocalRequestOptions options)
    {
        var message = _shared.InternalRuntime.RuntimeClient.CreateLocalRequest(this, request, options);
        _shared.InternalRuntime.MessagingTrace.OnDispatcherReceiveMessage(message);
        ReceiveRequest(message);
    }

    private void ReceiveResponse(Message message)
    {
        var state = State;
        if (state == ActivationState.Invalid)
        {
            _shared.InternalRuntime.MessagingTrace.OnDispatcherReceiveInvalidActivation(message, state);
            // 注意，即使激活实例无效，也始终处理响应。
        }
        else
        {
            _shared.MessagingProcessingInstruments.OnDispatcherMessageProcessedOk(message);
        }

        _shared.InternalRuntime.RuntimeClient.ReceiveResponse(message);
    }

    private void ReceiveRequest(Message message)
    {
        var overloadException = CheckOverloaded();
        if (overloadException != null && !message.IsLocalOnly)
        {
            _shared.MessagingProcessingInstruments.OnDispatcherMessageProcessedError(message);
            _shared.InternalRuntime.MessageCenter.RejectMessage(message, Message.RejectionTypes.Overloaded, overloadException, "Target activation is overloaded " + this);
            return;
        }

        lock (_lock)
        {
            _waitingRequests.Add((message, CoarseStopwatch.StartNew()));
        }

        _workSignal.Signal();
    }

    /// <summary>
    /// 拒绝此激活实例从队列中移除但未处理的请求。
    /// </summary>
    private void RejectRequest(Message message, Exception exception)
    {
        if (message.IsLocalOnly)
        {
            LocalRequests.Reject(message, exception, _shared.Logger);
        }
        else
        {
            _shared.InternalRuntime.MessageCenter.RejectMessage(message, Message.RejectionTypes.Transient, exception);
        }
    }

    /// <summary>
    /// 移除所有已排队的请求。调用方持有 <see cref="_lock"/>。
    /// </summary>
    /// <returns>请求集合；如果没有请求，则为 <see langword="null"/>。</returns>
    private List<Message>? TakeWaitingRequests()
    {
        if (_waitingRequests.Count == 0)
        {
            return null;
        }

        var requests = new List<Message>(_waitingRequests.Count);
        foreach (var (message, _) in _waitingRequests)
        {
            requests.Add(message);
        }

        _waitingRequests.Clear();
        return requests;
    }

    /// <summary>
    /// 移除并拒绝永不离开此激活实例的本地请求。
    /// </summary>
    private void RejectLocalOnlyRequests(List<Message> requests)
    {
        foreach (var message in requests)
        {
            if (message.IsLocalOnly)
            {
                RejectRequest(message, new OrleansMessageRejectionException($"Activation {this} is no longer valid."));
            }
        }

        requests.RemoveAll(static message => message.IsLocalOnly);
    }

    /// <summary>
    /// 拒绝或转发此激活实例在无法继续处理请求后从队列中移出的请求。
    /// </summary>
    /// <remarks>
    /// 如果这是重复的激活实例，或它曾成功激活，则转发请求；否则拒绝请求。
    /// 本地请求永不离开此激活实例，因此拒绝它们。
    /// </remarks>
    private void RejectOrForwardRequests(List<Message> requests)
    {
        RejectLocalOnlyRequests(requests);
        if (requests.Count == 0)
        {
            return;
        }

        var reject = DeactivationException is not null && ForwardingAddress is null;
        if (reject)
        {
            LogRejectAllQueuedMessages(_shared.Logger, requests.Count, this);
        }
        else if (_shared.Logger.IsEnabled(LogLevel.Debug))
        {
            if (ForwardingAddress is { } address)
            {
                LogReroutingMessages(_shared.Logger, requests.Count, this, address);
            }
            else
            {
                LogReroutingMessagesNoForwarding(_shared.Logger, requests.Count, this);
            }
        }

        _shared.InternalRuntime.GrainLocator.InvalidateCache(Address);
        _shared.InternalRuntime.MessageCenter.ProcessRequestsToInvalidActivation(
            requests,
            Address,
            ForwardingAddress,
            DeactivationReason.Description,
            DeactivationException,
            rejectMessages: reject);
    }

    #region Activation
    public void Rehydrate(IRehydrationContext context)
    {
        ScheduleOperation(new Command.Rehydrate(context));
    }

    public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
    {
        var metrics = CatalogInstruments.ActivationMetricTracker.Start(_shared.CatalogInstruments, IsUsingGrainDirectory, _shared.GrainTypeMetricName);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_shared.InternalRuntime.CollectionOptions.Value.ActivationTimeout);

        ScheduleOperation(new Command.Activate(requestContext, cts, metrics));
    }

    private async Task ActivateAsync(Dictionary<string, object>? requestContextData, CatalogInstruments.ActivationMetricTracker activationMetrics, CancellationToken cancellationToken)
    {
        if (State != ActivationState.Creating)
        {
            LogIgnoringActivateAttempt(_shared.Logger, this, State);
            return;
        }

        _activationActivity?.AddEvent(new ActivityEvent("activation-start"));
        try
        {
            if (IsUsingGrainDirectory)
            {
                bool success;
                Exception? registrationException;

                // 启动目录注册 Activity，作为激活 Activity 的子级。
                using (var registerSpan = _activationActivity is not null
                    ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.RegisterDirectoryEntry, ActivityKind.Internal, _activationActivity.Context)
                    : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.RegisterDirectoryEntry, ActivityKind.Internal))
                {
                    registerSpan?.SetTag(ActivityTagKeys.GrainId, GrainId.ToString());
                    registerSpan?.SetTag(ActivityTagKeys.SiloId, _shared.Runtime.SiloAddress.ToString());
                    registerSpan?.SetTag(ActivityTagKeys.ActivationId, ActivationId.ToString());
                    registerSpan?.SetTag(ActivityTagKeys.DirectoryPreviousRegistrationPresent,
                        PreviousRegistration is not null);
                    var previousRegistration = PreviousRegistration;
                    var verifiedRecoveryMembershipVersion = 0L;

                    try
                    {
                        while (true)
                        {
                            LogRegisteringGrain(_shared.Logger, this, previousRegistration);

                            var result = await _shared.InternalRuntime.GrainLocator
                                .Register(Address, previousRegistration, cancellationToken);
                            if (Address.Matches(result))
                            {
                                Address = result;

                                // 如果提交此注册时 DGD 恢复流程已推进，则在此激活实例变为有效之前
                                // 必须针对恢复后的视图重新注册。
                                if (_shared.GrainDirectory is DistributedGrainDirectory distributedGrainDirectory)
                                {
                                    var recoveryMembershipVersion = distributedGrainDirectory.RecoveryMembershipVersion;
                                    if (recoveryMembershipVersion > verifiedRecoveryMembershipVersion
                                        && recoveryMembershipVersion > result.MembershipVersion.Value)
                                    {
                                        verifiedRecoveryMembershipVersion = recoveryMembershipVersion;
                                        previousRegistration = result;
                                        _activationActivity?.AddEvent(new ActivityEvent("directory-register-retry-recovery"));
                                        registerSpan?.AddEvent(new ActivityEvent("retry-recovery"));
                                        continue;
                                    }
                                }

                                success = true;
                                _activationActivity?.AddEvent(new ActivityEvent("directory-register-success"));
                                registerSpan?.AddEvent(new ActivityEvent("success"));
                                registerSpan?.SetTag(ActivityTagKeys.DirectoryRegisteredAddress, result.ToFullString());
                            }
                            else if (result?.SiloAddress is { } registeredSilo &&
                                     registeredSilo.Equals(Address.SiloAddress))
                            {
                                previousRegistration = result;
                                LogAttemptToRegisterWithPreviousActivation(_shared.Logger, GrainId, result);
                                _activationActivity?.AddEvent(new ActivityEvent("directory-register-retry-previous"));
                                registerSpan?.AddEvent(new ActivityEvent("retry-previous"));
                                continue;
                            }
                            else
                            {
                                ForwardingAddress = result?.SiloAddress;
                                if (ForwardingAddress is { } address)
                                {
                                    DeactivationReason = new(DeactivationReasonCode.DuplicateActivation,
                                        $"This grain is active on another host ({address}).");
                                }

                                success = false;
                                _shared.CatalogInstruments.OnActivationConcurrentRegistrationAttempt(_shared.GrainTypeMetricName);
                                LogDuplicateActivation(
                                    _shared.Logger,
                                    Address,
                                    ForwardingAddress,
                                    GrainInstance?.GetType(),
                                    new(Address),
                                    WaitingCount);
                                _activationActivity?.AddEvent(new ActivityEvent("duplicate-activation"));
                                registerSpan?.AddEvent(new ActivityEvent("duplicate"));
                                if (ForwardingAddress is { } fwd)
                                {
                                    registerSpan?.SetTag(ActivityTagKeys.DirectoryForwardingAddress, fwd.ToString());
                                }
                            }

                            break;
                        }

                        registrationException = null;
                    }
                    catch (Exception exception)
                    {
                        registrationException = exception;
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            LogFailedToRegisterGrain(_shared.Logger, registrationException, this);
                        }

                        success = false;
                        _activationActivity?.AddEvent(new ActivityEvent("directory-register-failed"));
                        SetActivityError(registerSpan, exception, ActivityErrorEvents.DirectoryRegisterFailed);
                    }

                }
                if (!success)
                {
                    // 停用会取消激活令牌，因此先捕获目录操作的结果。
                    activationMetrics.DirectoryRegistrationFailed(registrationException, cancellationToken.IsCancellationRequested);
                    Deactivate(new(DeactivationReasonCode.DirectoryFailure, registrationException, "Failed to register activation in grain directory."), cancellationToken);

                    // 激活失败。
                    if (registrationException is not null)
                    {
                        SetActivityError(_activationActivity, registrationException, ActivityErrorEvents.ActivationCancelled);
                    }
                    else
                    {
                        SetActivityError(_activationActivity, ActivityErrorEvents.ActivationCancelled);
                    }

                    return;
                }
            }

            lock (_lock)
            {
                SetState(ActivationState.Activating);
            }
            _activationActivity?.AddEvent(new ActivityEvent("state-activating"));
            LogActivatingGrain(_shared.Logger, this);

            try
            {
                RequestContextExtensions.Import(requestContextData);
                try
                {
                    if (_lifecycle is { } lifecycle)
                    {
                        _activationActivity?.AddEvent(new ActivityEvent("lifecycle-start"));

                        // 激活超时会取消此令牌。运行时等待用户代码执行完成，因此执行期间激活实例
                        // 保留其注册和服务。如果令牌已取消，则激活失败。
                        await lifecycle.OnStart(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _activationActivity?.AddEvent(new ActivityEvent("lifecycle-started"));
                    }
                }
                catch (Exception exception)
                {
                    LogErrorStartingLifecycle(_shared.Logger, exception, this);
                    _activationActivity?.AddEvent(new ActivityEvent("lifecycle-start-failed"));
                    throw;
                }

                if (GrainInstance is IGrainBase grainBase)
                {
                    // 为 OnActivateAsync 的执行启动一个跟踪跨度。
                    using var onActivateSpan = _activationActivity is not null
                        ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.OnActivate, ActivityKind.Internal, _activationActivity.Context)
                        : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.OnActivate, ActivityKind.Internal);
                    if (onActivateSpan is { IsAllDataRequested: true })
                    {
                        onActivateSpan.SetTag(ActivityTagKeys.GrainId, GrainId.ToString());
                        onActivateSpan.SetTag(ActivityTagKeys.GrainType, _shared.GrainTypeName ?? GrainInstance.GetType().FullName);
                        onActivateSpan.SetTag(ActivityTagKeys.SiloId, _shared.Runtime.SiloAddress.ToString());
                        onActivateSpan.SetTag(ActivityTagKeys.ActivationId, ActivationId.ToString());
                    }

                    try
                    {
                        await grainBase.OnActivateAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (Exception exception)
                    {
                        if (cancellationToken.IsCancellationRequested && exception is ObjectDisposedException or OperationCanceledException)
                        {
                            _shared.CatalogInstruments.OnActivationFailedToActivate(_shared.GrainTypeMetricName);

                            // 用户代码已观察到激活取消，表现为抛出 OperationCanceledException，
                            // 或访问了因取消而释放的对象。
                            if (exception is ObjectDisposedException ode)
                            {
                                LogActivationDisposedObjectAccessed(_shared.Logger, ode.ObjectName, this);
                                Deactivate(
                                    new(DeactivationReasonCode.RuntimeRequested, ode,
                                        $"Disposed object {ode.ObjectName} referenced after cancellation of activation was requested."),
                                    CancellationToken.None);
                            }
                            else
                            {
                                Deactivate(
                                    new(DeactivationReasonCode.RuntimeRequested, exception,
                                        "Activation was cancelled by the runtime."), CancellationToken.None);
                            }

                            SetActivityError(_activationActivity, exception, ActivityErrorEvents.ActivationCancelled);
                            LogActivationCancelled(_shared.Logger, this, cancellationToken.IsCancellationRequested,
                                DeactivationReason.ReasonCode, DeactivationReason.Description, ForwardingAddress);
                            _activationActivity?.Dispose();
                            _activationActivity = null;
                            activationMetrics.Canceled();
                            return;
                        }

                        LogErrorInGrainMethod(_shared.Logger, exception, nameof(IGrainBase.OnActivateAsync), this);
                        SetActivityError(onActivateSpan, exception, ActivityErrorEvents.OnActivateFailed);
                        throw;
                    }
                }

                lock (_lock)
                {
                    if (State is ActivationState.Activating)
                    {
                        _idleDuration = CoarseStopwatch.StartNew();
                        SetState(ActivationState.Valid);
                        _shared.InternalRuntime.ActivationWorkingSet.OnActivated(this);
                    }
                }
                _activationActivity?.AddEvent(new ActivityEvent("state-valid"));
                _activationActivity?.Dispose();
                _activationActivity = null;

                GrainLifecycleEvents.EmitActivated(this);

                LogFinishedActivatingGrain(_shared.Logger, this);
                activationMetrics.Succeeded();
            }
            catch (Exception exception)
            {
                _shared.CatalogInstruments.OnActivationFailedToActivate(_shared.GrainTypeMetricName);
                activationMetrics.Failed(cancellationToken.IsCancellationRequested);
                var sourceException = (exception as OrleansLifecycleCanceledException)?.InnerException ?? exception;
                LogErrorActivatingGrain(_shared.Logger, sourceException, this);
                if (!cancellationToken.IsCancellationRequested)
                {
                    ScheduleOperation(new Command.Delay(TimeSpan.FromSeconds(5)));
                }
                Deactivate(new(DeactivationReasonCode.ActivationFailed, sourceException, "Failed to activate grain."), CancellationToken.None);
                SetActivityError(_activationActivity, ActivityErrorEvents.ActivationFailed);
                _activationActivity?.Dispose();
                _activationActivity = null;
                return;
            }
        }
        catch (Exception exception)
        {
            LogActivationFailed(_shared.Logger, exception, this);
            activationMetrics.Failed(cancellationToken.IsCancellationRequested);
            Deactivate(new(DeactivationReasonCode.ApplicationError, exception, "Failed to activate grain."), CancellationToken.None);
            SetActivityError(_activationActivity, ActivityErrorEvents.ActivationError);
            _activationActivity?.Dispose();
            _activationActivity = null;
        }
        finally
        {
            activationMetrics.Record();
            _workSignal.Signal();
        }
    }

    private void SetActivityError(Activity? erroredActivity, string? errorEventName)
    {
        if (erroredActivity is { } activity)
        {
            activity.SetStatus(ActivityStatusCode.Error, errorEventName);
        }
    }

    private void SetActivityError(Activity? erroredActivity, Exception exception, string? errorEventName)
    {
        if (erroredActivity is { } activity)
        {
            activity.SetStatus(ActivityStatusCode.Error, errorEventName);
            activity.SetTag(ActivityTagKeys.ExceptionType, exception.GetType().FullName);
            activity.SetTag(ActivityTagKeys.ExceptionMessage, exception.Message);
        }
    }

    #endregion

    #region Deactivation

    /// <summary>
    /// 完成停用流程。
    /// </summary>
    private async Task FinishDeactivating(Command.Deactivate deactivateCommand, CancellationToken cancellationToken)
    {
        using var _ = deactivateCommand.Activity;

        var deactivationMetrics = CatalogInstruments.DeactivationMetricTracker.Start(_shared.CatalogInstruments, _shared.GrainTypeMetricName);
        var migrating = false;
        ActivationMigration? migration = null;
        var encounteredError = false;
        try
        {
            try
            {
                LogCompletingDeactivation(_shared.Logger, this);

                // 阻止定时器触发。
                DisposeTimers();

                // 等待排他执行的代码不会在此激活实例上运行。
                CloseExclusiveSections();

                // 如果停用开始时 Grain 处于有效状态，则调用 OnDeactivateAsync。
                if (deactivateCommand.PreviousState == ActivationState.Valid)
                {
                    if (GrainInstance is IGrainBase grainBase)
                    {
                        // 为 OnDeactivateAsync 的执行启动一个跟踪跨度。

                        using var onDeactivateSpan = deactivateCommand.Activity is not null
                            ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.OnDeactivate, ActivityKind.Internal, parentContext: deactivateCommand.Activity.Context)
                            : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.OnDeactivate, ActivityKind.Internal);
                        if (onDeactivateSpan is { IsAllDataRequested: true })
                        {
                            onDeactivateSpan.SetTag(ActivityTagKeys.GrainId, GrainId.ToString());
                            onDeactivateSpan.SetTag(ActivityTagKeys.GrainType, _shared.GrainTypeName ?? GrainInstance.GetType().FullName);
                            onDeactivateSpan.SetTag(ActivityTagKeys.SiloId, _shared.Runtime.SiloAddress.ToString());
                            onDeactivateSpan.SetTag(ActivityTagKeys.ActivationId, ActivationId.ToString());
                            onDeactivateSpan.SetTag(ActivityTagKeys.DeactivationReason, DeactivationReason.ToString());
                        }

                        try
                        {
                            LogBeforeOnDeactivateAsync(_shared.Logger, this);

                            // 停用超时会取消此令牌。运行时会等待用户代码执行完成，然后才
                            // 注销激活实例或释放其服务。
                            await grainBase.OnDeactivateAsync(DeactivationReason, cancellationToken);

                            LogAfterOnDeactivateAsync(_shared.Logger, this);
                        }
                        catch (Exception exception)
                        {
                            LogErrorInGrainMethod(_shared.Logger, exception, nameof(IGrainBase.OnDeactivateAsync), this);
                            SetActivityError(onDeactivateSpan, exception, ActivityErrorEvents.OnDeactivateFailed);

                            // 吞掉异常，继续停用。
                            encounteredError = true;
                        }
                    }
                }

                try
                {
                    if (_lifecycle is { } lifecycle)
                    {
                        // 停止先前已启动的生命周期阶段。
                        // 忽略从未启动的阶段。
                        await lifecycle.OnStop(cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    LogErrorStartingLifecycle(_shared.Logger, exception, this);

                    // 吞掉异常，继续停用。
                    encounteredError = true;
                }

                // 所属 Silo 退出时，仍有排队请求的激活实例将迁移，并携带这些请求。
                // 只有已完成激活的实例才能迁移，因为被中断的激活可能持有部分初始化的状态。
                if (!encounteredError
                    && deactivateCommand.PreviousState == ActivationState.Valid
                    && DehydrationContext is null
                    && WaitingCount > 0
                    && MigratesOnRetirement())
                {
                    DehydrationContext = new(_shared.SerializerSessionPool, requestContext: null);
                }

                if (!encounteredError
                    && DehydrationContext is { } context
                    && _shared.MigrationManager is { } migrationManager
                    && !cancellationToken.IsCancellationRequested)
                {
                    migrating = await StartMigrationAsync(context, migrationManager, cancellationToken);
                }

                // 如果因目录故障而停用实例，则不应注销它。
                var isDirectoryFailure = DeactivationReason.ReasonCode is DeactivationReasonCode.DirectoryFailure;

                if (!migrating && IsUsingGrainDirectory && !cancellationToken.IsCancellationRequested && !isDirectoryFailure)
                {
                    // 从目录注销。
                    // 如果 Grain 已迁移，新激活实例将自行对注册记录执行检查并设置操作。
                    try
                    {
                        await _shared.InternalRuntime.GrainLocator.Unregister(Address, UnregistrationCause.Force).WaitAsync(cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            LogFailedToUnregisterActivation(_shared.Logger, exception, this);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetActivityError(deactivateCommand.Activity, ex, "Error in FinishDeactivating");
                LogErrorDeactivating(_shared.Logger, ex, this);
            }

            if (IsStuckDeactivating)
            {
                deactivationMetrics = deactivationMetrics.DeactivateStuckActivation();
                _shared.CatalogInstruments.ActivationShutdownViaDeactivateStuckActivation(_shared.GrainTypeMetricName);
            }
            else if (migrating)
            {
                deactivationMetrics = deactivationMetrics.Migration();
                _shared.CatalogInstruments.ActivationShutdownViaMigration(_shared.GrainTypeMetricName);
            }
            else if (_isInWorkingSet)
            {
                deactivationMetrics = deactivationMetrics.DeactivateOnIdle();
                _shared.CatalogInstruments.ActivationShutdownViaDeactivateOnIdle(_shared.GrainTypeMetricName);
            }
            else
            {
                deactivationMetrics = deactivationMetrics.Collection();
                _shared.CatalogInstruments.ActivationShutdownViaCollection(_shared.GrainTypeMetricName);
            }

            try
            {
                await DisposeAsync();
            }
            catch (Exception exception)
            {
                SetActivityError(deactivateCommand.Activity, exception, "Error in FinishDeactivating");
                LogExceptionDisposing(_shared.Logger, exception, this);
            }

            if (DeactivationStartTime is not null)
            {
                GrainLifecycleEvents.EmitDeactivated(this, DeactivationReason);
            }

            deactivationMetrics = deactivationMetrics.Record();

            // 激活实例直到此时才离开激活实例目录，因此激活实例目录会等待它的整个停用流程完成，在完成之前不会创建该 Grain 的新本地激活实例。
            // 离开目录、转发排队请求和发出停用信号均为同步操作，且转发发生在离开目录之后，以防转发的请求回到此激活实例。
            UnregisterMessageTarget();
            try
            {
                if (migration is not null)
                {
                    _shared.InternalRuntime.GrainLocator.InvalidateCache(Address);
                    migration.Deliver();
                }
            }
            finally
            {
                try
                {
                    // 不属于初始移交批次的请求仍由此激活实例拥有。
                    List<Message>? queued;
                    lock (_lock)
                    {
                        queued = TakeWaitingRequests();
                    }

                    if (queued is not null)
                    {
                        RejectOrForwardRequests(queued);
                    }
                }
                finally
                {
                    GetDeactivationCompletionSource().TrySetResult(true);
                }
            }

            _workSignal.Signal();
        }
        finally
        {
            deactivationMetrics.RecordIfNeeded();
        }

        async ValueTask<bool> StartMigrationAsync(DehydrationContextHolder context, IActivationMigrationManager migrationManager, CancellationToken cancellationToken)
        {
            try
            {
                if (ForwardingAddress is null)
                {
                    var selectedAddress = await PlaceMigratingGrainAsync(context.RequestContext, cancellationToken);
                    if (selectedAddress is null)
                    {
                        return false;
                    }

                    ForwardingAddress = selectedAddress;
                }

                // 填充状态提取上下文。
                if (context.RequestContext is { } requestContext)
                {
                    RequestContextExtensions.Import(requestContext);
                }

                OnDehydrate(context.MigrationContext);

                // 将状态提取上下文和排队请求发送至目标宿主，目标通过其常规请求队列接纳这些请求。
                // 迁移完成之前，此激活实例仍留在激活实例目录中，因此发往此 Silo 上该 Grain 的请求仍会到达它。
                // 取消会停止尚未发送的迁移。已经发送的迁移将在目标响应时完成。
                migration = migrationManager.StartMigration(ForwardingAddress, Address, context.MigrationContext, TakeRequestsForMigration(), cancellationToken);
                await migration.Completion;
                _shared.InternalRuntime.GrainLocator.UpdateCache(GrainId, ForwardingAddress);
                return true;
            }
            catch (Exception exception)
            {
                LogFailedToMigrateActivation(_shared.Logger, exception, this);
                return false;
            }
        }
    }

    private TaskCompletionSource<bool> GetDeactivationCompletionSource()
    {
        lock (_lock)
        {
            _extras ??= new();
            return _extras.DeactivationTask ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    ValueTask IGrainManagementExtension.DeactivateOnIdle() =>
        ((IGrainManagementExtension)this).DeactivateOnIdle(default);

    ValueTask IGrainManagementExtension.DeactivateOnIdle(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deactivate(new(DeactivationReasonCode.ApplicationRequested, $"{nameof(IGrainManagementExtension.DeactivateOnIdle)} was called."), cancellationToken);
        return default;
    }

    ValueTask IGrainManagementExtension.MigrateOnIdle() =>
        ((IGrainManagementExtension)this).MigrateOnIdle(default);

    async ValueTask IGrainManagementExtension.MigrateOnIdle(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestContextData = RequestContext.CallContextData?.Value.Values;
        var selectedAddress = await PlaceMigratingGrainAsync(requestContextData, cancellationToken);
        if (selectedAddress is null)
        {
            return;
        }

        // 仅当选中另一个 Silo 时才迁移。
        ForwardingAddress = selectedAddress;
        LogDebugMigrating(_shared.Logger, GrainId, ForwardingAddress);
        Migrate(requestContextData, cancellationToken);
    }

    private async ValueTask<SiloAddress?> PlaceMigratingGrainAsync(Dictionary<string, object>? requestContextData, CancellationToken cancellationToken)
    {
        try
        {
            var placementService = _shared.Runtime.ServiceProvider.GetRequiredService<PlacementService>();
            var selectedAddress = await placementService.PlaceGrainAsync(GrainId, requestContextData, PlacementStrategy)
                .WaitAsync(cancellationToken);

            if (selectedAddress is null)
            {
                // 没有为此 Grain 选中合适的 Silo。
                LogDebugPlacementStrategyFailedToSelectDestination(_shared.Logger, PlacementStrategy, GrainId);
                return null;
            }
            else if (selectedAddress.Equals(_shared.Runtime.SiloAddress))
            {
                // 可能是因为这是此 Grain 唯一（兼容）的 Silo，或是放置决策器出于其他原因选择了此
                // Silo。
                LogDebugPlacementStrategySelectedCurrentSilo(_shared.Logger, PlacementStrategy, GrainId);
                return null;
            }

            return selectedAddress;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogErrorSelectingMigrationDestination(_shared.Logger, exception, GrainId);
            return null;
        }
    }

    private void UnregisterMessageTarget()
    {
        _shared.InternalRuntime.Catalog.UnregisterMessageTarget(this);
    }

    void ICallChainReentrantGrainContext.OnEnterReentrantSection(Guid reentrancyId)
    {
        var tracker = GetComponent<ReentrantRequestTracker>();
        if (tracker is null)
        {
            tracker = new ReentrantRequestTracker();
            SetComponent(tracker);
        }

        tracker.EnterReentrantSection(reentrancyId);
    }

    void ICallChainReentrantGrainContext.OnExitReentrantSection(Guid reentrancyId)
    {
        var tracker = GetComponent<ReentrantRequestTracker>();
        if (tracker is null)
        {
            throw new InvalidOperationException("Attempted to exit reentrant section without entering it.");
        }

        tracker.LeaveReentrantSection(reentrancyId);
    }

    private bool IsReentrantSection(Guid reentrancyId)
    {
        if (reentrancyId == Guid.Empty)
        {
            return false;
        }

        var tracker = GetComponent<ReentrantRequestTracker>();
        if (tracker is null)
        {
            return false;
        }

        return tracker.IsReentrantSectionActive(reentrancyId);
    }

    ValueTask IGrainCallCancellationExtension.CancelRequestAsync(
        GrainId senderGrainId,
        CorrelationId messageId,
        CancellationToken cancellationToken)
        => this.RunOrQueueTask(
            static state => state.self.CancelRequestAsyncCore(state.senderGrainId, state.messageId, state.cancellationToken),
            (self: this, senderGrainId, messageId, cancellationToken));

    private ValueTask CancelRequestAsyncCore(
        GrainId senderGrainId,
        CorrelationId messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCancelRequest())
        {
            // 正在取消的消息可能尚未到达，因此重试几次。
            return RetryCancellationAfterDelay();
        }

        return ValueTask.CompletedTask;

        async ValueTask RetryCancellationAfterDelay()
        {
            var attemptsRemaining = 3;
            do
            {
                await Task.Delay(1_000, cancellationToken);
            } while (!TryCancelRequest() && --attemptsRemaining > 0);
        }

        bool TryCancelRequest()
        {
            Message? message = null;
            var wasWaiting = false;
            lock (_lock)
            {
                // 检查正在运行的请求。
                foreach (var candidate in _runningRequests.Keys)
                {
                    if (candidate.Id == messageId && candidate.SendingGrain == senderGrainId)
                    {
                        message = candidate;
                        break;
                    }
                }

                if (message is null)
                {
                    // 检查等待中的请求。
                    for (var i = 0; i < _waitingRequests.Count; i++)
                    {
                        var candidate = _waitingRequests[i].Message;
                        if (candidate.Id == messageId && candidate.SendingGrain == senderGrainId)
                        {
                            message = candidate;
                            _waitingRequests.RemoveAt(i);
                            wasWaiting = true;
                            break;
                        }
                    }
                }
            }

            var didCancel = false;
            if (message is not null && message.BodyObject is IInvokable request)
            {
                if (wasWaiting)
                {
                    // 如果请求此前处于等待状态，则必定已成功取消，因此现在发送响应。
                    _shared.InternalRuntime.RuntimeClient.SendResponse(message, Response.FromException(new OperationCanceledException()));
                    didCancel = true;
                }
                else
                {
                    didCancel = TryCancelInvokable(request) || !request.IsCancellable;
                }
            }

            return didCancel;
        }

        bool TryCancelInvokable(IInvokable request)
        {
            try
            {
                return request.TryCancel();
            }
            catch (Exception exception)
            {
                LogErrorCancellationCallbackFailed(Shared.Logger, exception);
                return true;
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "One or more cancellation callbacks failed."
    )]
    private static partial void LogErrorCancellationCallbackFailed(ILogger logger, Exception exception);

    #endregion

    /// <summary>
    /// 激活实例生命周期的大部分时间中不需要的额外属性。
    /// </summary>
    private class ActivationDataExtra : Dictionary<object, object>
    {
        private const int IsStuckProcessingMessageFlag = 1 << 0;
        private const int IsStuckDeactivatingFlag = 1 << 1;
        private const int IsDisposingFlag = 1 << 2;
        private byte _flags;

        /// <summary>
        /// 阻止空闲回收的 <see cref="IActivationKeepAlive"/> 持有次数。
        /// </summary>
        public int KeepAliveHolds;

        public HashSet<IGrainTimer>? Timers { get => GetValueOrDefault<HashSet<IGrainTimer>>(nameof(Timers)); set => SetOrRemoveValue(nameof(Timers), value); }

        /// <summary>
        /// 在状态恢复期间，这可能包含此 Grain 先前（刚提取状态）的激活实例地址。
        /// </summary>
        public GrainAddress? PreviousRegistration { get => GetValueOrDefault<GrainAddress>(nameof(PreviousRegistration)); set => SetOrRemoveValue(nameof(PreviousRegistration), value); }

        /// <summary>
        /// 如果 State == Invalid，这可能包含传入消息的转发地址。
        /// </summary>
        public SiloAddress? ForwardingAddress { get => GetValueOrDefault<SiloAddress>(nameof(ForwardingAddress)); set => SetOrRemoveValue(nameof(ForwardingAddress), value); }

        /// <summary>
        /// Grain 停用完成时完成的 <see cref="TaskCompletionSource{TResult}"/>。
        /// </summary>
        public TaskCompletionSource<bool>? DeactivationTask { get => GetDeactivationInfoOrDefault()?.DeactivationTask; set => EnsureDeactivationInfo().DeactivationTask = value; }

        public TaskCompletionSource? ActivationReady { get => GetValueOrDefault<TaskCompletionSource>(nameof(ActivationReady)); set => SetOrRemoveValue(nameof(ActivationReady), value); }

        public DateTime? DeactivationStartTime { get => GetDeactivationInfoOrDefault()?.DeactivationStartTime; set => EnsureDeactivationInfo().DeactivationStartTime = value; }

        public DeactivationReason DeactivationReason { get => GetDeactivationInfoOrDefault()?.DeactivationReason ?? default; set => EnsureDeactivationInfo().DeactivationReason = value; }

        /// <summary>
        /// 迁移到其他位置时，其中包含需要在激活实例之间保留的信息。
        /// </summary>
        public DehydrationContextHolder? DehydrationContext { get => GetValueOrDefault<DehydrationContextHolder>(nameof(DehydrationContext)); set => SetOrRemoveValue(nameof(DehydrationContext), value); }

        /// <summary>
        /// 等待已开启的排他区段关闭的排他区段。
        /// </summary>
        public ExclusiveSectionWaiters? ExclusiveSectionWaiters { get => GetValueOrDefault<ExclusiveSectionWaiters>(nameof(ExclusiveSectionWaiters)); set => SetOrRemoveValue(nameof(ExclusiveSectionWaiters), value); }

        /// <summary>
        /// 已开启排他区段持有者的调用链可重入性 ID，无区段开启时为 <see cref="Guid.Empty"/>，
        /// 持有者没有调用链时也是如此。仅存储非空 ID。
        /// </summary>
        public Guid ExclusiveSectionReentrancyId
        {
            get => TryGetValue(nameof(ExclusiveSectionReentrancyId), out var id) ? (Guid)id : Guid.Empty;
            set => SetOrRemoveValue(nameof(ExclusiveSectionReentrancyId), value == Guid.Empty ? null : value);
        }

        private DeactivationInfo? GetDeactivationInfoOrDefault() => GetValueOrDefault<DeactivationInfo>(nameof(DeactivationInfo));
        private DeactivationInfo EnsureDeactivationInfo()
        {
            ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(this, nameof(DeactivationInfo), out _);
            info ??= new DeactivationInfo();
            return (DeactivationInfo)info;
        }

        public bool IsStuckProcessingMessage { get => GetFlag(IsStuckProcessingMessageFlag); set => SetFlag(IsStuckProcessingMessageFlag, value); }
        public bool IsStuckDeactivating { get => GetFlag(IsStuckDeactivatingFlag); set => SetFlag(IsStuckDeactivatingFlag, value); }
        public bool IsDisposing { get => GetFlag(IsDisposingFlag); set => SetFlag(IsDisposingFlag, value); }

        private void SetFlag(int flag, bool value)
        {
            if (value)
            {
                _flags |= (byte)flag;
            }
            else
            {
                _flags &= (byte)~flag;
            }
        }

        private bool GetFlag(int flag) => (_flags & flag) != 0;
        private T? GetValueOrDefault<T>(object key)
        {
            TryGetValue(key, out var result);
            return (T?)result;
        }

        private void SetOrRemoveValue(object key, object? value)
        {
            if (value is null)
            {
                Remove(key);
            }
            else
            {
                base[key] = value;
            }
        }

        private sealed class DeactivationInfo
        {
            public DateTime? DeactivationStartTime;
            public DeactivationReason DeactivationReason;
            public TaskCompletionSource<bool>? DeactivationTask;
        }
    }

    private abstract class Command(CancellationTokenSource cts) : IDisposable
    {
        private bool _disposed;
        private readonly Lock _lock = new();
        private readonly CancellationTokenSource _cts = cts;
        public CancellationToken CancellationToken => _cts.Token;

        public virtual void Cancel()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _cts.Cancel();
            }
        }

        public virtual void Dispose()
        {
            try
            {
                lock (_lock)
                {
                    _disposed = true;
                    _cts.Dispose();
                }
            }
            catch
            {
                // 忽略。
            }

            GC.SuppressFinalize(this);
        }

        public sealed class Deactivate(CancellationTokenSource cts, ActivationState previousState, Activity? activity) : Command(cts)
        {
            public ActivationState PreviousState { get; } = previousState;
            public Activity? Activity { get; } = activity;
        }

        public sealed class Activate(Dictionary<string, object>? requestContext, CancellationTokenSource cts, CatalogInstruments.ActivationMetricTracker metrics) : Command(cts)
        {
            public Dictionary<string, object>? RequestContext { get; } = requestContext;
            public CatalogInstruments.ActivationMetricTracker Metrics { get; } = metrics;
        }

        public sealed class Rehydrate(IRehydrationContext context) : Command(new())
        {
            public readonly IRehydrationContext Context = context;

            public override void Dispose()
            {
                base.Dispose();
                (Context as IDisposable)?.Dispose();
            }
        }

        public sealed class Delay(TimeSpan duration) : Command(new())
        {
            public TimeSpan Duration { get; } = duration;
        }
    }

    internal class ReentrantRequestTracker : Dictionary<Guid, int>
    {
        public void EnterReentrantSection(Guid reentrancyId)
        {
            Debug.Assert(reentrancyId != Guid.Empty);
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(this, reentrancyId, out _);
            ++count;
        }

        public void LeaveReentrantSection(Guid reentrancyId)
        {
            Debug.Assert(reentrancyId != Guid.Empty);
            ref var count = ref CollectionsMarshal.GetValueRefOrNullRef(this, reentrancyId);
            if (Unsafe.IsNullRef(ref count))
            {
                return;
            }

            if (--count <= 0)
            {
                Remove(reentrancyId);
            }
        }

        public bool IsReentrantSectionActive(Guid reentrancyId)
        {
            Debug.Assert(reentrancyId != Guid.Empty);
            return TryGetValue(reentrancyId, out var count) && count > 0;
        }
    }

    private class DehydrationContextHolder(SerializerSessionPool sessionPool, Dictionary<string, object>? requestContext)
    {
        public readonly MigrationContext MigrationContext = new(sessionPool);
        public readonly Dictionary<string, object>? RequestContext = requestContext;

        /// <summary>
        /// 发起迁移的 Grain 调用的 Activity 上下文。
        /// 用于将状态提取的跟踪跨度关联为迁移请求跟踪的子级。
        /// </summary>
        public ActivityContext? MigrationActivityContext { get; set; } = Activity.Current?.Context;
    }

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_Reject_ActivationTooManyRequests,
        Level = LogLevel.Warning,
        Message = "Overload - {Count} enqueued requests for activation {Activation}, exceeding hard limit rejection threshold of {HardLimit}"
    )]
    private static partial void LogRejectActivationTooManyRequests(ILogger logger, int count, ActivationData activation, int hardLimit);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_Warn_ActivationTooManyRequests,
        Level = LogLevel.Warning,
        Message = "Hot - {Count} enqueued requests for activation {Activation}, exceeding soft limit warning threshold of {SoftLimit}"
    )]
    private static partial void LogWarnActivationTooManyRequests(ILogger logger, int count, ActivationData activation, int softLimit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error while cancelling on-going operation '{Operation}'."
    )]
    private static partial void LogErrorCancellingOperation(ILogger logger, Exception exception, object operation);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Migrating {GrainId} to {SiloAddress}"
    )]
    private static partial void LogDebugMigrating(ILogger logger, GrainId grainId, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error while selecting a migration destination for {GrainId}"
    )]
    private static partial void LogErrorSelectingMigrationDestination(ILogger logger, Exception exception, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Placement strategy {PlacementStrategy} failed to select a destination for migration of {GrainId}"
    )]
    private static partial void LogDebugPlacementStrategyFailedToSelectDestination(ILogger logger, PlacementStrategy placementStrategy, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Placement strategy {PlacementStrategy} selected the current silo as the destination for migration of {GrainId}"
    )]
    private static partial void LogDebugPlacementStrategySelectedCurrentSilo(ILogger logger, PlacementStrategy placementStrategy, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error invoking MayInterleave predicate on grain {Grain} for message {Message}"
    )]
    private static partial void LogErrorInvokingMayInterleavePredicate(ILogger logger, Exception exception, ActivationData grain, Message message);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error in ProcessOperationsAsync for grain activation '{Activation}'."
    )]
    private static partial void LogErrorInProcessOperationsAsync(ILogger logger, Exception exception, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Rehydrating grain '{GrainContext}' from previous activation."
    )]
    private static partial void LogRehydratingGrain(ILogger logger, ActivationData grainContext);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ignoring attempt to rehydrate grain '{GrainContext}' in the '{State}' state."
    )]
    private static partial void LogIgnoringRehydrateAttempt(ILogger logger, ActivationData grainContext, ActivationState state);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Previous activation address was {PreviousRegistration}"
    )]
    private static partial void LogPreviousActivationAddress(ILogger logger, GrainAddress previousRegistration);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Rehydrated grain from previous activation"
    )]
    private static partial void LogRehydratedGrain(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error while rehydrating activation"
    )]
    private static partial void LogErrorRehydratingActivation(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Dehydrating grain activation"
    )]
    private static partial void LogDehydratingActivation(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Dehydrated grain activation"
    )]
    private static partial void LogDehydratedActivation(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error while dehydrating activation"
    )]
    private static partial void LogErrorDehydratingActivation(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_RerouteAllQueuedMessages,
        Level = LogLevel.Debug,
        Message = "Rejecting {Count} messages from invalid activation {Activation}."
    )]
    private static partial void LogRejectAllQueuedMessages(ILogger logger, int count, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Registering grain '{Grain}' in activation directory. Previous known registration is '{PreviousRegistration}'.")]
    private static partial void LogRegisteringGrain(ILogger logger, ActivationData grain, GrainAddress? previousRegistration);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The grain directory has an existing entry pointing to a different activation of this grain, '{GrainId}', on this silo: '{PreviousRegistration}'."
            + " This may indicate that the previous activation was deactivated but the directory was not successfully updated."
            + " The directory will be updated to point to this activation."
    )]
    private static partial void LogAttemptToRegisterWithPreviousActivation(ILogger logger, GrainId grainId, GrainAddress previousRegistration);

    [LoggerMessage(
        EventId = (int)ErrorCode.Dispatcher_ExtendedMessageProcessing,
        Level = LogLevel.Warning,
        Message = "Current request has been active for {CurrentRequestActiveTime} for grain {Grain}. Currently executing {BlockingRequest}. Trying to enqueue {Message}.")]
    private static partial void LogWarningDispatcher_ExtendedMessageProcessing(
        ILogger logger,
        TimeSpan currentRequestActiveTime,
        ActivationDataLogValue grain,
        Message blockingRequest,
        Message message);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Abandoning stuck deactivating activation {Activation}. ForwardingAddress={ForwardingAddress}")]
    private static partial void LogWarningAbandoningStuckDeactivatingActivation(ILogger logger, ActivationData activation, SiloAddress? forwardingAddress);

    private readonly struct ActivationDataLogValue(ActivationData activation, bool includeExtraDetails = false)
    {
        public override string ToString() => activation.ToDetailedString(includeExtraDetails);
    }

    [LoggerMessage(
        EventId = (int)ErrorCode.Runtime_Error_100064,
        Level = LogLevel.Warning,
        Message = "Failed to register grain {Grain} in grain directory")]
    private static partial void LogFailedToRegisterGrain(ILogger logger, Exception exception, ActivationData grain);


    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Ignoring activation request for {Grain} because this grain is in the '{State}' state")]
    private static partial void LogIgnoringActivateAttempt(ILogger logger, ActivationData grain, ActivationState state);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_BeforeCallingActivate,
        Level = LogLevel.Debug,
        Message = "Activating grain {Grain}")]
    private static partial void LogActivatingGrain(ILogger logger, ActivationData grain);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error starting lifecycle for activation '{Activation}'")]
    private static partial void LogErrorStartingLifecycle(ILogger logger, Exception exception, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error thrown from {MethodName} for activation '{Activation}'")]
    private static partial void LogErrorInGrainMethod(ILogger logger, Exception exception, string methodName, ActivationData activation);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_AfterCallingActivate,
        Level = LogLevel.Debug,
        Message = "Finished activating grain {Grain}")]
    private static partial void LogFinishedActivatingGrain(ILogger logger, ActivationData grain);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_ErrorCallingActivate,
        Level = LogLevel.Error,
        Message = "Error activating grain {Grain}")]
    private static partial void LogErrorActivatingGrain(ILogger logger, Exception exception, ActivationData grain);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_DisposedObjectAccess,
        Level = LogLevel.Warning,
        Message = "Disposed object {ObjectName} accessed in OnActivateAsync for grain {Grain}. Ensure the cancellationToken is passed to all async methods or they have .WaitAsync(cancellationToken) called on them.")]
    private static partial void LogActivationDisposedObjectAccessed(ILogger logger, string objectName, ActivationData grain);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_CancelledActivate,
        Level = LogLevel.Information,
        Message = "Activation was cancelled for {Grain}. CancellationRequested={CancellationRequested}, DeactivationReasonCode={DeactivationReasonCode}, DeactivationReason={DeactivationReason}, ForwardingAddress={ForwardingAddress}"
    )]
    private static partial void LogActivationCancelled(ILogger logger, ActivationData grain, bool cancellationRequested, DeactivationReasonCode deactivationReasonCode, string? deactivationReason, SiloAddress? forwardingAddress);
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Completing deactivation of '{Activation}'")]
    private static partial void LogCompletingDeactivation(ILogger logger, ActivationData activation);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_BeforeCallingDeactivate,
        Level = LogLevel.Debug,
        Message = "About to call OnDeactivateAsync for '{Activation}'")]
    private static partial void LogBeforeOnDeactivateAsync(ILogger logger, ActivationData activation);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_AfterCallingDeactivate,
        Level = LogLevel.Debug,
        Message = "Returned from calling '{Activation}' OnDeactivateAsync method")]
    private static partial void LogAfterOnDeactivateAsync(ILogger logger, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to unregister activation '{Activation}' from directory")]
    private static partial void LogFailedToUnregisterActivation(ILogger logger, Exception exception, ActivationData activation);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_DeactivateActivation_Exception,
        Level = LogLevel.Warning,
        Message = "Error deactivating '{Activation}'")]
    private static partial void LogErrorDeactivating(ILogger logger, Exception exception, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Exception disposing activation '{Activation}'")]
    private static partial void LogExceptionDisposing(ILogger logger, Exception exception, ActivationData activation);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to migrate activation '{Activation}'")]
    private static partial void LogFailedToMigrateActivation(ILogger logger, Exception exception, ActivationData activation);

    private readonly struct FullAddressLogRecord(GrainAddress address)
    {
        public override string ToString() => address.ToFullString();
    }

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_DuplicateActivation,
        Level = LogLevel.Debug,
        Message = "Tried to create a duplicate activation {Address}, but we'll use {ForwardingAddress} instead. GrainInstance type is {GrainInstanceType}. Full activation address is {FullAddress}. We have {WaitingCount} messages to forward")]
    private static partial void LogDuplicateActivation(
        ILogger logger,
        GrainAddress address,
        SiloAddress? forwardingAddress,
        Type? grainInstanceType,
        FullAddressLogRecord fullAddress,
        int waitingCount);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_RerouteAllQueuedMessages,
        Level = LogLevel.Debug,
        Message = "Rerouting {NumMessages} messages from invalid grain activation {Grain} to {ForwardingAddress}")]
    private static partial void LogReroutingMessages(ILogger logger, int numMessages, ActivationData grain, SiloAddress forwardingAddress);

    [LoggerMessage(
        EventId = (int)ErrorCode.Catalog_RerouteAllQueuedMessages,
        Level = LogLevel.Debug,
        Message = "Rerouting {NumMessages} messages from invalid grain activation {Grain}")]
    private static partial void LogReroutingMessagesNoForwarding(ILogger logger, int numMessages, ActivationData grain);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Activation of grain {Grain} failed")]
    private static partial void LogActivationFailed(ILogger logger, Exception exception, ActivationData grain);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error in grain message loop"
    )]
    private static partial void LogErrorInGrainMessageLoop(ILogger logger, Exception exception);
}
