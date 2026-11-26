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
/// 保持 Orleans 内部业务所需的额外每次激活状态。
/// 激活状态的突变在内部同步进行。
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

    // 专栏是否开放:只请总是中断的请求,并按科持单人的要求
    // 链,开始。见 IGrainRequestAdmission.EnterExclusiveSectionAsync。
    private bool _exclusiveSectionOpen;
    private bool _isInWorkingSet = true;
    private CoarseStopwatch _busyDuration;
    private CoarseStopwatch _idleDuration;
    private GrainReference? _selfReference;
    private IActivationCollectionRegistration? _collectionRegistration;

    // 所需价值较少,而且由于物体大小的原因,不需要靠激活直接生活。
    // 此字段中的值通常用于表示激活状态的终止或非
    // 全部 grains使用,例如 grain 计时器。
    private ActivationDataExtra? _extras;

    // 代表此激活信息循环的任务 。
    // 此字段被指定, 从未读取, 并且仅用于调试目的( 例如, 在存储堆放处中, 将循环任务与激活连接) 。
#pragma warning disable IDE0052 // Remove unread private members
    private Task? _messageLoopTask;
#pragma warning restore IDE0052 // Remove unread private members

    private Activity? _activationActivity;

    /// <summary>
    /// 活动错误事件名称常数,激活寿命周期使用。
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
    /// 如果有的话, 则获取激活活动的活动环境 。
    /// 这使儿童活动能够在激活生命周期运作期间适当亲子关系。
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

    // 目前,唯一支持的多活动 grain 是采用 StatelessWorkerPlacement 战略。
    internal bool IsStatelessWorker => PlacementStrategy is StatelessWorkerPlacement;

    /// <summary>
    /// 返回值,以表明是否需要启动程序注册
    /// grain 目录。
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

            // 开放的独家部分运行代码,例如交易, 不需要请求。
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
    /// 如果知道, 将获得上一个目录注册 grain。
    /// 该目录用于更新 grain 目录,以指向激活期间的新注册。
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
    /// 检查此激活是否超载 。
    /// 返回 LimitExceededException 如果超载,否则 <c>null</c>c>
    /// </summary>
    /// <returns>返回 LimitExceededException 如果超载,否则 <c>null</c>c></returns>
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

        if (maxRequestsHardLimit <= 0 && maxRequestsSoftLimit <= 0) return null; // 没有规定限制

        int count = GetRequestCount();

        if (maxRequestsHardLimit > 0 && count > maxRequestsHardLimit) // 硬限
        {
            LogRejectActivationTooManyRequests(_shared.Logger, count, this, maxRequestsHardLimit);
            return new LimitExceededException(limitName, count, maxRequestsHardLimit, ToString());
        }

        if (maxRequestsSoftLimit > 0 && count > maxRequestsSoftLimit) // 软限制
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
    /// 返回此激活闲置了多久 。
    /// </summary>
    public TimeSpan GetIdleness() => _idleDuration.Elapsed;

    /// <summary>
    /// 返回此激活是否闲置时间已足够长, 以收集 。
    /// </summary>
    public bool IsStale() => GetIdleness() >= _shared.CollectionAgeLimit;

    public void DelayDeactivation(TimeSpan timespan)
    {
        var rescheduleCollection = false;
        lock (_lock)
        {
            if (timespan == TimeSpan.MaxValue || timespan == Timeout.InfiniteTimeSpan)
            {
                // 如果将这些值添加到当前时间, 将会溢出, 所以直接使用 DateTime.MaxValue 。
                KeepAliveUntil = DateTime.MaxValue;
            }
            else if (timespan <= TimeSpan.Zero)
            {
                // 取消上一个 DelayDeactivation , 并恢复到正常的收集行为 。
                // 如果有一个活动保存的、重新安排时间的收藏,可以收集 grain
                // 之后 CollectionAgeLimit 而不是等待先前预定的远期。
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

            // 解除活动或处置的激活不得再进入工作场景。
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
            // 如果 grain 正在激活,请取消该操作。
            if (_pendingOperations is not { Count: > 0 } operations)
            {
                return;
            }

            var opCount = operations.Count;
            // 我们可以用 ArrayPool<Command> 需要把
            // 命令型,这意味着我们需要环绕_Oput Opers
            // 使拍摄快照的目的落空。
            // 注: CopyTo 不使用队列的量子, 所以它的安全性可以这样抓取快照 。
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

            // 如果尚未开始移徙,则设定一个移徙背景,以涵盖任何应当转移的国家。
            // 向解除作用进程发出信号,即正在发生移徙,因此重要的是,在我们开始解除作用之前必须这样做。
            DehydrationContext ??= new(_shared.SerializerSessionPool, requestContext);

            if (State is not ActivationState.Deactivating)
            {
                // 开始拆除 grain ,为移民做准备。
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

        // 将 grain 标为除去活动状态,以便转发电文而不是被援引
        Deactivate(reason, cancellationToken: default);

        // 尝试从目录和目录中删除此激活
        // 这使得启动程序无法进行,使目前请求的处理卡住,直到最终完成
        // (此时可能永远不会发生这种情况,因为如果 grain 被认为卡住了,那么很可能存在某种
        // 应用程序错误, 也许是僵局)
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
        lock (_lock) // 需要锁定, 因为可以将处置点调用在 grain 上下文外的终局线( 不使用单线) 。
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

            // 需要设置为空的计时器, 因为 OnTimerDisposed 如果定时器不是空的, 则要将定时器变换为空 。
            var timers = Timers;
            Timers = null;

            // 停止所有计时器
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

                // 检查他们执行多久了
                var executionTime = runDuration.Elapsed;
                if (executionTime >= slowRunningRequestDuration)
                {
                    // 中断信件 X 执行已很长时间
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
                    // 信件 X 已排入 Y 的目标 grain , 目前处于要处理的队列中的位置 QueueLength 。
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
#pragma warning disable RS0030 // IDisposable requires synchronously completing activation cleanup.
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
            // 忽略。 忽略。
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

            // 如果不是尚未移除,就是如果目前仍在运行,这种情况将留在工作套件中。
            _isInWorkingSet = !wouldRemove || !inactive;
            return inactive;
        }
    }

    private async Task RunMessageLoop()
    {
        // 请注意,这个环圈永远不会终止。 这看起来可能很奇怪, 但有原因:
        // a grain 总是必须接受和处理任何接收的信息。 grain 进程
        // 这些信息信息由 grain状态决定。如果 grain 尚未确定
        // 已完成激活, 它会让信件继续排队直到它完成激活 。
        // 如果 grain 无法激活,则信件将被拒绝回复。
        // 如果 grain 终止,则信息将转发给此 grain的新实例。
        // 当 grain 被停用且没有
        // 扎实的引用。
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
            // 启动程序将无法处理的请求被从锁的队列中移除,被拒绝或转发
            // 可以在其他启动程序上传递信息,并满足当地请求。
            // 激活此操作时, 请求进一步队列 。
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

                    // 如果激活无效, 拒绝所有待处理信件, 除了本地端信件 。
                    // 仅使用本地信息用于内部系统操作,在 grain 有效或失效时不应拒绝。
                    if (State != ActivationState.Valid && !(message.IsLocalOnly && State is ActivationState.Deactivating))
                    {
                        unprocessableRequests = DequeueUnprocessableRequests();
                        break;
                    }

                    try
                    {
                        if (!MayInvokeRequest(message))
                        {
                            // 激活无法马上处理此信件, 请尝试下一条消息 。
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
                                    // 考虑:处理关于再入激活的长期请求检测 -- -- 这个逻辑只适用于非再入启动
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

                        // 如果当前信息不兼容, 将停止激活并最终将信息传递给新的化身 。
                        if (message.InterfaceVersion > 0)
                        {
                            var compatibilityDirector = _shared.InternalRuntime.CompatibilityDirectorManager.GetDirector(message.InterfaceType);
                            var currentVersion = _shared.InternalRuntime.GrainVersionManifest.GetLocalVersion(message.InterfaceType);
                            if (!compatibilityDirector.IsCompatible(message.InterfaceVersion, currentVersion))
                            {
                                // 添加此激活到缓存失效信头 。
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

                    // 从队列中删除信件到任何进程或拒绝它。
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

                // 开始在锁定外引用消息
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

            // 这个逻辑只适用于非再生激活
            // 考虑:处理长时间请求的重新激活探测。
            _blockingRequest = message;
            _busyDuration = stopwatch;
        }

        // 锁定了锁,一旦启动后无法处理等待请求,就取消其请求
        // 返回无效,而它可能仍然处理它们。
        List<Message>? DequeueUnprocessableRequests()
        {
            if (State is ActivationState.Creating or ActivationState.Activating)
            {
                // 在激活有效或无效之前不做任何动作
                return null;
            }

            if (State is ActivationState.Deactivating)
            {
                // 确定是否宣布此激活被卡
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
                    // 在 grain 仍然在停止活动且未被宣布卡住时不转发信件, 因为它们
                    // 将转发到同一个 grain 实例。
                    return null;
                }
            }

            return TakeWaitingRequests();
        }

        void AbandonStuckDeactivatingActivation()
        {
            var forwardingAddress = ForwardingAddress;
            LogWarningAbandoningStuckDeactivatingActivation(_shared.Logger, this, forwardingAddress);

            // 在停止使用达到 StartMigrationAsync之前,移民目标没有得到证明。
            // 如果之前停止失效卡住, 重新发送信件, 而不是转发给
            // 目标可能没有有效的替换激活 。
            ForwardingAddress = null;
            UnregisterMessageTarget();
            _shared.InternalRuntime.GrainLocator.Unregister(Address, UnregistrationCause.Force).Ignore();
            GetDeactivationCompletionSource().TrySetResult(true);
        }

        bool MayInvokeRequest(Message incoming)
        {
            if (_exclusiveSectionOpen)
            {
                // 只有在申请总是中断和请求时,才在一期间开始对部分持有人的通话链链启动
                // 专有部分打开。
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

            // 否则,如果 grain 重新被批准或电文可以中断使用,则允许请求方援引
            if (incoming.IsAlwaysInterleave)
            {
                return true;
            }

            if (_runningNonAlwaysInterleaveCount == 0
                || (incoming.IsReadOnly && _runningNonAlwaysInterleaveWritableCount == 0))
            {
                return true;
            }

            // 处理呼叫链中继
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

                    // 删除上一个操作。
                    // 操作在完成之前不会被移除,以便彼此见面。
                    // 例如,解除功能请求可以看到任何正在进行的激活请求并取消。
                    if (op is not null)
                    {
                        _pendingOperations.Dequeue();
                    }

                    // 尝试获得下一个操作。
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
                // 开始补水的间隔
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
                // 将母活动背景从脱水环境持有者处获取(在移民启动时捕获)
                var parentContext = DehydrationContext?.MigrationActivityContext;

                var grainMigrationParticipant = GrainInstance as IGrainMigrationParticipant;

                if (grainMigrationParticipant is not null)
                {
                    // 启动脱水间隔, 与触发脱水的移民请求相匹配
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

                // 请注意,这些电话是从体液酸盐转过来的,不是出于任何特定原因,而只是对称。
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
    /// 处理收到的消息和队列/ voke 合适的处理器
    /// </summary>
    /// <param name="message"></param>
    private void InvokeIncomingRequest(Message message)
    {
        _shared.MessagingProcessingInstruments.OnDispatcherMessageProcessedOk(message);

        try
        {
            var task = _shared.InternalRuntime.RuntimeClient.Invoke(this, message);

            // 注:所有结果----成功或过失均由此运行,
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
    /// 当激活完成交易并可能准备进行其他交易时,即启动程序被启动
    /// </summary>
    /// <param name="message">刚刚完成处理的电文。</param>
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

            // 如果信件意在保持激活活动, 重置闲置计时器并确保激活
            // 正在激活工作组中。
            if (message.IsKeepAlive)
            {
                MarkActive();
            }

            // 以下逻辑仅适用于非返回启动
            if (_blockingRequest is null || message.Equals(_blockingRequest))
            {
                _blockingRequest = null;
                _busyDuration = default;
            }
        }

        // 信号信息泵, 以查看是否有另一个请求, 现在该请求已经完成后可以处理
        _workSignal.Signal();
    }

    // 持单人的电话链在这里读取,在打电话者的流中同步。
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
            // 取消服务员排队等候,下一个出口跳过它。
            using (cancellationToken.UnsafeRegister(static (state, token) => ((ExclusiveSectionWaiter)state!).TrySetCanceled(token), waiter))
            {
                await waiter.Task;
            }

            return new ExclusiveSection(activation);
        }
    }

    // 关闭开放的部分,或用持单者的呼叫链将它交给下一个等待部分,保持打开。
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

            // 等待部分的请求,或下一位持有人的电话链上的请求,现在可以开始。
            signal = _waitingRequests.Count > 0 && (next is null || next.ReentrancyId != Guid.Empty);
        }

        if (signal)
        {
            _workSignal.Signal();
        }
    }

    // 返回第一个接受打开区域或无等待时为空的等候区。必须在 _lock下调用该区。
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

    // 破坏激活完成后等待停止失效的部分部分的损坏:打开部分的代码可能永远无法结束,因为
    // 停止功能不会等待代码的出现,而代码不是请求, 而后来的部分无法打开。
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

    // 引爆的开放独家部分,它处理一次关闭。
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

    // 等待打开部分关闭的一节, 并附上要求该部分的代码的呼叫链回存标识 。
    private sealed class ExclusiveSectionWaiter(Guid reentrancyId) : TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public Guid ReentrancyId { get; } = reentrancyId;
    }

    // 等待打开部分按请求的顺序关闭的各节。
    private sealed class ExclusiveSectionWaiters : Queue<ExclusiveSectionWaiter>
    {
        // 一旦激活完成后, 设定启动将停止活动, 之后会等待错误的部分 。
        public Exception? Closed { get; set; }
    }

    /// <summary>
    /// 恢复闲置时间,并确保激活在工作组中。必须调用 <see cref="_lock"/>。
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

        // 不要处理已经超时的消息
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
        else // 请求或 OneWay
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
            // 请注意,我们总是处理反应,即使启动无效。
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
    /// 拒绝请求, 而此激活程序不经处理就从队列中删除了的请求 。
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
    /// 删除所有队列中的请求。 调用者持有 <see cref="_lock"/>。
    /// </summary>
    /// <returns>(a) 请求,或 <see langword="null"/> 如果无。</returns>
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
    /// 移除和拒绝本地请求, 永远不要离开激活 。
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
    /// 拒绝或转发请求,一旦无法处理,启动后即予以取消。
    /// </summary>
    /// <remarks>
    /// 如果请求是重复激活或某一时刻成功启动,则转交请求,否则被驳回。
    /// 当地请求永远不能离开启动程序,所以被拒绝。
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

                // 启动活动孩子的启动活动启动时开始目录登记活动
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

                                // 如果在进行登记期间,决定指导文件的追回提前,重新登记
                                // 激活有效前相对于已恢复的视图。
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
                    // 取消激活取消激活的标记, 所以先捕捉目录结果 。
                    activationMetrics.DirectoryRegistrationFailed(registrationException, cancellationToken.IsCancellationRequested);
                    Deactivate(new(DeactivationReasonCode.DirectoryFailure, registrationException, "Failed to register activation in grain directory."), cancellationToken);

                    // 启动失败 。
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

                        // 激活超时取消此标记。 运行时间等待用户代码完成, 因此激活会保持
                        // 代码运行时, 它的注册和服务会运行。 如果该标记被取消, 激活将失败 。
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
                    // 开始 OnActivateAsync 执行的间隔
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

                            // 用户代码观察到启动取消, 要么扔 OperationCanceledException
                            // 或通过访问因取消而处置的物体。
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
    /// 完成解除作用的过程。
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

                // 停止计时器发射
                DisposeTimers();

                // 等待运行的代码不会运行在激活上 。
                CloseExclusiveSections();

                // 如果在解除功能启动时 grain 有效,请呼叫 OnDeactivateAsync。
                if (deactivateCommand.PreviousState == ActivationState.Valid)
                {
                    if (GrainInstance is IGrainBase grainBase)
                    {
                        // 开始 OnDeactivateAsync 执行的间隔

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

                            // 禁用取消超时取消此标记。 运行时间等待用户代码完成后才能取消
                            // 解除对激活或处置其服务的登记。
                            await grainBase.OnDeactivateAsync(DeactivationReason, cancellationToken);

                            LogAfterOnDeactivateAsync(_shared.Logger, this);
                        }
                        catch (Exception exception)
                        {
                            LogErrorInGrainMethod(_shared.Logger, exception, nameof(IGrainBase.OnDeactivateAsync), this);
                            SetActivityError(onDeactivateSpan, exception, ActivityErrorEvents.OnDeactivateFailed);

                            // 吞噬例外情形,并继续进行停止使用。
                            encounteredError = true;
                        }
                    }
                }

                try
                {
                    if (_lifecycle is { } lifecycle)
                    {
                        // 停止以前开始的生命周期阶段。
                        // 从未开始的阶段被忽略。
                        await lifecycle.OnStop(cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    LogErrorStartingLifecycle(_shared.Logger, exception, this);

                    // 吞噬例外情形,并继续进行停止使用。
                    encounteredError = true;
                }

                // 其 silo 退休时,仍然有排队申请的启动程序随申请一起迁移。
                // 只有完成激活的启动程序才能迁移,因为中断的启动程序可能保持部分初始化状态。
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

                // 如果由于目录故障而撤销了这个案例,我们不应该解除登记。
                var isDirectoryFailure = DeactivationReason.ReasonCode is DeactivationReasonCode.DirectoryFailure;

                if (!migrating && IsUsingGrainDirectory && !cancellationToken.IsCancellationRequested && !isDirectoryFailure)
                {
                    // 从目录中解密 。
                    // 如果 grain 被迁移,新激活将对登记本身进行检查和设置。
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

            // 激活现在只能留下目录,所以目录等待全部停止运行,在完成 grain 之前没有创建新的本地启动。
            // 离开、转交排队请求和信号停止运行是同步的,转发随后即离开,以便转发的请求不再恢复到激活状态。
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
                    // 启动后仍拥有原始交割范围之外的请求。
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

                // 弹出脱水环境
                if (context.RequestContext is { } requestContext)
                {
                    RequestContextExtensions.Import(requestContext);
                }

                OnDehydrate(context.MigrationContext);

                // 将脱水背景和排队请求发送目标主机, 并通过普通的请列接收它们 。
                // 启动程序仍保留在目录中,直到迁移完成,这样,达到此 silo 的 grain 申请就可以到达该目录。
                // 取消取消停止未发送的迁移。 当目标响应时, 发送的取消会完成 。
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

        // 仅在选择了不同的 silo 时才迁移。
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
                // 不适用 silo 为此选中了 grain.
                LogDebugPlacementStrategyFailedToSelectDestination(_shared.Logger, PlacementStrategy, GrainId);
                return null;
            }
            else if (selectedAddress.Equals(_shared.Runtime.SiloAddress))
            {
                // 这可能是因为这是唯一的(兼容的) silo 排排 grain 是因为安置主任选择了
                // silo 还有其他原因
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
            // 正在取消的消息可能尚未到来, 所以重试几次 。
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
                // 检查运行中的请求 。
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
                    // 检查等待请求
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
                    // 如果请求在等待,那么我们一定设法取消了请求,所以现在就发送答复。
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
    /// 激活的大多数生命周期不需要的额外特性。
    /// </summary>
    private class ActivationDataExtra : Dictionary<object, object>
    {
        private const int IsStuckProcessingMessageFlag = 1 << 0;
        private const int IsStuckDeactivatingFlag = 1 << 1;
        private const int IsDisposingFlag = 1 << 2;
        private byte _flags;

        /// <summary>
        /// 防止闲置收藏的 <see cref="IActivationKeepAlive"/> 持有数。
        /// </summary>
        public int KeepAliveHolds;

        public HashSet<IGrainTimer>? Timers { get => GetValueOrDefault<HashSet<IGrainTimer>>(nameof(Timers)); set => SetOrRemoveValue(nameof(Timers), value); }

        /// <summary>
        /// 在补液过程中,这可能包含前一次(最近脱水)激活 grain的地址。
        /// </summary>
        public GrainAddress? PreviousRegistration { get => GetValueOrDefault<GrainAddress>(nameof(PreviousRegistration)); set => SetOrRemoveValue(nameof(PreviousRegistration), value); }

        /// <summary>
        /// 如果国家 = 无效,此地址中可能包含接收信件的转发地址
        /// </summary>
        public SiloAddress? ForwardingAddress { get => GetValueOrDefault<SiloAddress>(nameof(ForwardingAddress)); set => SetOrRemoveValue(nameof(ForwardingAddress), value); }

        /// <summary>
        /// A <see cref="TaskCompletionSource{TResult}"/> 完成时, grain 已经解除了作用。
        /// </summary>
        public TaskCompletionSource<bool>? DeactivationTask { get => GetDeactivationInfoOrDefault()?.DeactivationTask; set => EnsureDeactivationInfo().DeactivationTask = value; }

        public TaskCompletionSource? ActivationReady { get => GetValueOrDefault<TaskCompletionSource>(nameof(ActivationReady)); set => SetOrRemoveValue(nameof(ActivationReady), value); }

        public DateTime? DeactivationStartTime { get => GetDeactivationInfoOrDefault()?.DeactivationStartTime; set => EnsureDeactivationInfo().DeactivationStartTime = value; }

        public DeactivationReason DeactivationReason { get => GetDeactivationInfoOrDefault()?.DeactivationReason ?? default; set => EnsureDeactivationInfo().DeactivationReason = value; }

        /// <summary>
        /// 当迁移到另一个地点时, 包含信息以保存整个激活 。
        /// </summary>
        public DehydrationContextHolder? DehydrationContext { get => GetValueOrDefault<DehydrationContextHolder>(nameof(DehydrationContext)); set => SetOrRemoveValue(nameof(DehydrationContext), value); }

        /// <summary>
        /// 等待开放专栏关闭的排版部分。
        /// </summary>
        public ExclusiveSectionWaiters? ExclusiveSectionWaiters { get => GetValueOrDefault<ExclusiveSectionWaiters>(nameof(ExclusiveSectionWaiters)); set => SetOrRemoveValue(nameof(ExclusiveSectionWaiters), value); }

        /// <summary>
        /// 开放专栏独开部分持有人的呼叫链回转识别码,即无区段时 <see cref="Guid.Empty"/>
        /// ,或者其持有者没有呼叫链。仅存储非空白的ID。
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
                // 忽略。 忽略。
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
        /// 发起移徙的 grain 呼吁的活动背景。
        /// 用于将脱水宽度定位到移民请求跟踪时。
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
