using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orleans.Diagnostics;
using Orleans.Serialization.Buffers;
using Orleans.Runtime.Internal;

namespace Orleans.Durable.Journaling;

internal partial class JournaledStateManager : IJournaledStateManager, IJournalStorageConsumer, ILifecycleParticipant<IGrainLifecycle>, ILifecycleObserver, IDisposable
{
    private const uint MinApplicationJournalStreamId = 8u;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IStateMachine> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, IStateMachine> _statesMap = [];

    private readonly JournaledStateManagerShared _shared;
    private readonly IJournalStorage _storage;
    private readonly IGrainContext? _grainContext;
    private readonly JournalBufferWriter _journalWriter;
    private readonly SingleWaiterAutoResetEvent _workSignal = new() { RunContinuationsAsynchronously = true };
    private readonly Queue<WorkItem> _workQueue = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly StateDirectory _journalStreamDirectory;
    private readonly RetiredStateTracker _retirementTracker;
    private Task? _workLoop;
    private ManagerState _state;
    private long _deletionGeneration;
    private Exception? _failure;
    private bool _migrationSnapshotRequired;
    private int _disposed;

    public JournaledStateManager(JournaledStateManagerShared shared, IJournalStorageProvider storageProvider, IGrainContext grainContext)
        : this(shared, CreateStorage(storageProvider, CreateJournalId(grainContext)), grainContext)
    {
        try
        {
            ((ILifecycleParticipant<IGrainLifecycle>)this).Participate(grainContext.ObservableLifecycle);
        }
        catch
        {
            ((IDisposable)this).Dispose();
            throw;
        }
    }

    public JournaledStateManager(JournaledStateManagerShared shared, IJournalStorageProvider storageProvider, JournalId journalId)
        : this(shared, CreateStorage(storageProvider, journalId))
    {
    }

    internal JournaledStateManager(
        JournaledStateManagerShared shared,
        IJournalStorage storage,
        IGrainContext? grainContext = null)
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(storage);
        _shared = shared;
        _storage = storage;
        _grainContext = grainContext;
        var serviceProvider = ServiceProvider;
        var journalStreamIdsCodec = JournalFormatServices.GetRequiredCommandCodec<IDurableDictionaryCommandCodec<string, uint>>(serviceProvider, WriteJournalFormatKey);
        var retirementTrackerCodec = JournalFormatServices.GetRequiredCommandCodec<IDurableDictionaryCommandCodec<string, DateTime>>(serviceProvider, WriteJournalFormatKey);
        _journalWriter = _shared.JournalFormat.CreateWriter();

        // 已知国家名单本身以耐久状态储存,含隐含代号0。
        // 这样我们就能够收回国家ids清单,而不必单独储存。
        _journalStreamDirectory = new StateDirectory(this, journalStreamIdsCodec);
        _statesMap[StateDirectory.Id] = _journalStreamDirectory;

        // 退休跟踪器是一个特殊的内部状态,有固定的ID。
        // 它不储存在_journalStream Timory,也不参加通用名称> ID 映射。
        _retirementTracker = new RetiredStateTracker(this, retirementTrackerCodec);
        _statesMap[RetiredStateTracker.Id] = _retirementTracker;
    }

    private static JournalId CreateJournalId(IGrainContext grainContext)
    {
        ArgumentNullException.ThrowIfNull(grainContext);
        return JournalId.FromGrainId(grainContext.GrainId);
    }

    internal static IJournalStorage CreateStorage(IJournalStorageProvider storageProvider, JournalId journalId)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        return storageProvider.CreateStorage(journalId) ?? throw new InvalidOperationException("The configured journal storage provider returned null.");
    }

    internal string WriteJournalFormatKey => _shared.JournalFormatKey;

    internal IServiceProvider ServiceProvider => _grainContext is { } context ? context.ActivationServices : _shared.ServiceProvider;

    public bool TryGetStateMachine(string name, [NotNullWhen(true)] out IStateMachine? stateMachine)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            ThrowIfFenced();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_states.TryGetValue(name, out var existing) && existing is not RetiredStateMachine)
            {
                stateMachine = existing;
                return true;
            }
        }

        stateMachine = null;
        return false;
    }

    protected void EnsureRegistrationAllowed()
    {
        lock (_lock)
        {
            ThrowIfFenced();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _shutdownCancellation.Token.ThrowIfCancellationRequested();
            if (_workLoop is not null)
            {
                throw new InvalidOperationException("New states cannot be registered after journaled state manager initialization has begun.");
            }
        }
    }

    public void RegisterStateMachine(string name, IStateMachine stateMachine)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stateMachine);

        lock (_lock)
        {
            EnsureRegistrationAllowed();
            if (_states.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException(
                    $"A state named '{name}' is already registered with type '{existing.GetType()}' and cannot be registered as '{stateMachine.GetType()}'.");
            }

            _states.Add(name, stateMachine);
            _workQueue.Enqueue(new RegisterStateWorkItem(name));
        }

        _workSignal.Signal();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task task;
        bool didEnqueue;
        lock (_lock)
        {
            _shutdownCancellation.Token.ThrowIfCancellationRequested();
            ThrowIfFenced();
            if (_workLoop is null)
            {
                _workLoop = Start();
            }

            task = EnqueueOrGetPendingWorkItem<InitializeWorkItem>(out didEnqueue);
        }

        if (didEnqueue)
        {
            _workSignal.Signal();
        }

        await task.WaitAsync(cancellationToken);
    }

    private Task Start()
    {
        using var suppressExecutionContext = new ExecutionContextSuppressor();
        return WorkLoop();
    }

    private async Task WorkLoop()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
        while (!_shutdownCancellation.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(_shutdownCancellation.Token).ConfigureAwait(true);
                _workSignal.Signal();
                break;
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                try
                {
                    LogErrorProcessingWorkItems(_shared.Logger, exception);
                }
                finally
                {
                    lock (_lock)
                    {
                        FaultQueuedWorkItemsUnderLock(exception);
                    }
                }
            }

            // 信号可以保留在失败尝试中。 只对新排队的初始化工作重试 。
            while (true)
            {
                await _workSignal.WaitAsync().ConfigureAwait(true);
                if (_shutdownCancellation.IsCancellationRequested)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_workQueue.Count > 0)
                    {
                        break;
                    }
                }
            }
        }

        while (!_shutdownCancellation.Token.IsCancellationRequested)
        {
            try
            {
                await _workSignal.WaitAsync().ConfigureAwait(true);
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

                while (true)
                {
                    WorkItem workItem;
                    lock (_lock)
                    {
                        if (!_workQueue.TryDequeue(out var dequeuedWorkItem))
                        {
                            // 等待队列再次信号 。
                            break;
                        }

                        workItem = dequeuedWorkItem;
                    }

                    var processingTimestamp = _shared.TimeProvider.GetTimestamp();
                    var queueOperation = GetStorageQueueOperation(workItem);
                    var queueDuration = queueOperation is null
                        ? default
                        : _shared.TimeProvider.GetElapsedTime(workItem.EnqueuedTimestamp, processingTimestamp);
                    var recordQueueDuration = workItem is DeleteStateWorkItem;
                    Activity? storageActivity = queueOperation is null
                        ? null
                        : ActivitySources.StorageGrainSource.StartActivity(
                            $"journal {queueOperation}",
                            ActivityKind.Internal,
                            workItem.TraceParent,
                            tags: null,
                            links: workItem.TraceLinks);
                    if (storageActivity is not null)
                    {
                        storageActivity.SetTag(ActivityTagKeys.JournalStorageOperation, queueOperation);
                    }

                    try
                    {
                        if (workItem is AppendJournalWorkItem or WriteSnapshotWorkItem
                            && workItem.DeletionGeneration != Volatile.Read(ref _deletionGeneration))
                        {
                            throw new InvalidOperationException(
                                "The journaled state operation was queued before deletion reset its state.");
                        }

                        if (workItem is AppendJournalWorkItem or WriteSnapshotWorkItem)
                        {
                            try
                            {
                                await PrepareStatesAsync().ConfigureAwait(true);
                            }
                            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
                            {
                                workItem.TrySetCanceled(_shutdownCancellation.Token);
                                return;
                            }
                        }

                        // 请注意,每项指令的执行都包含在内部,以避免分配不必要的重复状态。
                        // 我们为内部循环的绩效 牺牲了某个代码组织
                        switch (workItem)
                        {
                            case AppendJournalWorkItem:
                            case WriteSnapshotWorkItem:
                                {
                                    // TODO: 决定是快照还是附加最好。 例如, 通过对最新快照的大小和当前日记长度进行总结 。
                                    //       如果当前日记的长度大于快照大小, 则用快照代替附加更多日记条目 。
                                    var isSnapshot = workItem is WriteSnapshotWorkItem
                                        || _migrationSnapshotRequired
                                        || _storage.IsCompactionRequested;
                                    var operationLabel = isSnapshot
                                        ? JournalingInstruments.OperationSnapshot
                                        : JournalingInstruments.OperationAppend;
                                    if (isSnapshot)
                                    {
                                        var compactionReason = workItem is WriteSnapshotWorkItem
                                            ? JournalingInstruments.CompactionReasonUserSnapshot
                                            : _migrationSnapshotRequired
                                                ? JournalingInstruments.CompactionReasonMigration
                                                : JournalingInstruments.CompactionReasonStorageRequested;
                                        _shared.Instruments.OnCompactionTriggered(compactionReason);
                                    }
                                    _shared.Instruments.OnWriteCoalesced(operationLabel, workItem.CallerCount);
                                    var gatherStartTimestamp = _shared.TimeProvider.GetTimestamp();
                                    var statesScanned = 0L;
                                    ArcBuffer committedBuffer = default;
                                    ArcBuffer bufferToConsume = default;
                                    var hasCommittedBuffer = false;
                                    var hasBufferToConsume = false;
                                    var bufferToConsumeIsCommittedBuffer = false;
                                    var writeVersion = 0L;

                                    // 每个写入捕捉每个状态, 包括更改, 另一个线索正在写入到条目中
                                    // 写入等待该条目的关闭,无论它是附加还是
                                    // 替换日记,以便它捕捉到变化。
                                    while (true)
                                    {
                                        Task? activeEntryClosed;
                                        lock (_lock)
                                        {
                                            activeEntryClosed = _journalWriter.GetActiveEntryClosedTask();
                                            if (activeEntryClosed is null && isSnapshot)
                                            {
                                                using var snapshotWriter = _shared.JournalFormat.CreateWriter();
                                                if (_retirementTracker.Count > 0)
                                                {
                                                    RetireOrResurectStates();
                                                }

                                                if (_migrationSnapshotRequired)
                                                {
                                                    ThrowIfMigrationBlockedByRetiredStates();
                                                }

                                                // 国家身份图本身是作为耐久状态储存的,有ID 0。
                                                // 因为它包括所有其他国家的身份, 而重播日记时需要这些身份。
                                                // 如果我们撤走退休的州,这个画面将维持这一变化。
                                                AppendUpdatesOrSnapshotState(snapshotWriter, isSnapshot: true, StateDirectory.Id, _journalStreamDirectory);
                                                statesScanned++;

                                                foreach (var (id, state) in _statesMap)
                                                {
                                                    if (id is 0 || state is null)
                                                    {
                                                        continue;
                                                    }

                                                    AppendUpdatesOrSnapshotState(snapshotWriter, isSnapshot: true, id, state);
                                                    statesScanned++;
                                                }

                                                bufferToConsume = _journalWriter.GetCommittedBuffer();
                                                if (bufferToConsume.Length > 0)
                                                {
                                                    hasBufferToConsume = true;
                                                }
                                                else
                                                {
                                                    bufferToConsume.Dispose();
                                                }

                                                committedBuffer = snapshotWriter.GetCommittedBuffer();
                                            }
                                            else if (activeEntryClosed is null)
                                            {
                                                // 国家身份图本身是作为耐久状态储存的,有ID 0。
                                                // 因为它包括所有其他国家的身份, 而重播日记时需要这些身份。
                                                AppendUpdatesOrSnapshotState(_journalWriter, isSnapshot: false, StateDirectory.Id, _journalStreamDirectory);
                                                statesScanned++;

                                                foreach (var (id, state) in _statesMap)
                                                {
                                                    if (id is 0 || state is null)
                                                    {
                                                        continue;
                                                    }

                                                    AppendUpdatesOrSnapshotState(_journalWriter, isSnapshot: false, id, state);
                                                    statesScanned++;
                                                }

                                                committedBuffer = _journalWriter.GetCommittedBuffer();
                                                bufferToConsume = committedBuffer;
                                                bufferToConsumeIsCommittedBuffer = true;
                                            }

                                            if (activeEntryClosed is null)
                                            {
                                                if (committedBuffer.Length == 0)
                                                {
                                                    // 没有任何预谋,所以书写没有记录任何有待确认的内容。
                                                    committedBuffer.Dispose();
                                                    if (bufferToConsumeIsCommittedBuffer)
                                                    {
                                                        bufferToConsume = default;
                                                    }
                                                }
                                                else
                                                {
                                                    hasCommittedBuffer = true;
                                                    if (!isSnapshot)
                                                    {
                                                        hasBufferToConsume = true;
                                                    }

                                                    writeVersion = OnWriteCaptured();
                                                }
                                            }
                                        }

                                        if (activeEntryClosed is null)
                                        {
                                            break;
                                        }

                                        await activeEntryClosed.WaitAsync(_shutdownCancellation.Token).ConfigureAwait(true);

                                        // 捕获跟踪在完整准备的通行证上,没有交出 所以条目的变化和
                                        // 正在准备等待的写作时, 将先进行跨左转旋转的更改 。
                                        await PrepareStatesAsync().ConfigureAwait(true);
                                    }

                                    _shared.Instruments.OnGather(
                                        operationLabel,
                                        _shared.TimeProvider.GetElapsedTime(gatherStartTimestamp),
                                        statesScanned);

                                    if (!hasCommittedBuffer && hasBufferToConsume && !bufferToConsumeIsCommittedBuffer)
                                    {
                                        bufferToConsume.Dispose();
                                    }

                                    if (hasCommittedBuffer)
                                    {
                                        recordQueueDuration = true;
                                        var writeSequence = committedBuffer.AsReadOnlySequence();
#if DEBUG
                                        // 防御:将序列复制成集合缓冲,以便毒死它
                                        // 存储调回后返回。 IJournalStorage 执行中承诺的
                                        // 保留过去任务完成的顺序(违反有记录的
                                        // AppendAsync/ReplaceAsync合同下一个时, 将读为 0x67 字节
                                        // 触摸缓冲,在测试时大声地显示错误,而不是让
                                        // 回收的池数据隐藏在其中。
                                        var debugPoisonLength = checked((int)writeSequence.Length);
                                        var debugPoisonBuffer = ArrayPool<byte>.Shared.Rent(debugPoisonLength);
                                        writeSequence.CopyTo(debugPoisonBuffer);
                                        writeSequence = new ReadOnlySequence<byte>(debugPoisonBuffer, 0, debugPoisonLength);
#endif

                                        try
                                        {
                                            if (isSnapshot)
                                            {
                                                await ReplaceStorageAsync(writeSequence, _shutdownCancellation.Token).ConfigureAwait(true);
                                            }
                                            else
                                            {
                                                await AppendStorageAsync(writeSequence, _shutdownCancellation.Token).ConfigureAwait(true);
                                            }

                                            lock (_lock)
                                            {
                                                if (hasBufferToConsume)
                                                {
                                                    _journalWriter.Consume(bufferToConsume);
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            committedBuffer.Dispose();
                                            if (hasBufferToConsume && !bufferToConsumeIsCommittedBuffer)
                                            {
                                                bufferToConsume.Dispose();
                                            }
#if DEBUG
                                            debugPoisonBuffer.AsSpan(0, debugPoisonLength).Fill(0x67);
                                            ArrayPool<byte>.Shared.Return(debugPoisonBuffer);
#endif
                                        }
                                    }

                                    if (writeVersion != 0)
                                    {
                                        // 只有在每个州 的待处理条目被捕获后才能确认
                                        lock (_lock)
                                        {
                                            foreach (var state in _states.Values)
                                            {
                                                state.OnWriteCompleted();
                                            }

                                            OnWriteAcknowledged(writeVersion);
                                            if (isSnapshot)
                                            {
                                                _migrationSnapshotRequired = false;
                                            }
                                        }
                                    }

                                    break;
                                }

                            case DeleteStateWorkItem:
                                {
                                    // 安全储存
                                    await DeleteStorageAsync(_shutdownCancellation.Token).ConfigureAwait(true);

                                    lock (_lock)
                                    {
                                        // 丢弃正在重置的状态条目, 以便写入新目录 。
                                        _journalWriter.Reset();

                                        // 重置状态代码收藏 。
                                        _journalStreamDirectory.ResetVolatileState();

                                        // 为每个州分配新的州代号。
                                        // 这样做将引发重置,因为_journalStream 董事局将对有关国家具有约束力。
                                        foreach (var (name, state) in _states)
                                        {
                                            var id = _journalStreamDirectory.GetNextJournalStreamId();
                                            _journalStreamDirectory.Set(name, id);
                                        }

                                        _deletionGeneration++;
                                    }
                                    break;
                                }

                            case InitializeWorkItem:
                                {
                                    lock (_lock)
                                    {
                                        ThrowIfFenced();
                                        _state = ManagerState.Ready;
                                    }
                                    break;
                                }

                            case RegisterStateWorkItem registerState:
                                {
                                    lock (_lock)
                                    {
                                        if (_state is not ManagerState.Unknown)
                                        {
                                            throw new NotSupportedException("Registering a state after activation is not supported.");
                                        }

                                        var name = registerState.Name;
                                        if (!_journalStreamDirectory.ContainsKey(name))
                                        {
                                            // 这样做将引发重置,因为_journalStream 董事局将对有关国家具有约束力。
                                            _journalStreamDirectory.Set(name, _journalStreamDirectory.GetNextJournalStreamId());
                                        }
                                    }
                                    break;
                                }

                            default:
                                {
                                    Debug.Fail($"The command {workItem.GetType().FullName} is unsupported");
                                    break;
                                }
                        }

                        if (recordQueueDuration && queueOperation is not null)
                        {
                            _shared.Instruments.OnStorageOperationQueued(queueOperation, queueDuration, succeeded: true);
                        }

                        storageActivity?.SetStatus(ActivityStatusCode.Ok);
                        workItem.SetResult();
                    }
                    catch (StatePreparationFailedException exception)
                    {
                        // 仅准备投出这种类型的武器,所以没有记录任何情况:有待修改的改动仍为
                        // 明示重试, 管理器继续 。
                        LogStatePreparationFailed(_shared.Logger, exception.InnerException!);
                        storageActivity?.SetStatus(ActivityStatusCode.Error, "Journal state preparation failed.");
                        workItem.SetException(new JournaledStatePreCommitException(exception.InnerException!));
                    }
                    catch (Exception exception)
                    {
                        if (recordQueueDuration && queueOperation is not null)
                        {
                            _shared.Instruments.OnStorageOperationQueued(queueOperation, queueDuration, succeeded: false);
                        }

                        if (storageActivity is not null)
                        {
                            storageActivity.SetStatus(ActivityStatusCode.Error, exception.Message);
                            if (storageActivity.IsAllDataRequested)
                            {
                                storageActivity.SetTag(ActivityTagKeys.ExceptionType, exception.GetType().FullName);
                                storageActivity.SetTag(ActivityTagKeys.ExceptionMessage, exception.Message);
                                storageActivity.SetTag(ActivityTagKeys.ExceptionStacktrace, exception.ToString());
                                storageActivity.SetTag(ActivityTagKeys.ExceptionEscaped, true);
                            }
                        }

                        try
                        {
                            Fence(exception);
                        }
                        finally
                        {
                            workItem.SetException(exception);
                        }

                        return;
                    }
                    finally
                    {
                        storageActivity?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Fence(exception);
                return;
            }
        }
    }

    /// <summary>
    /// 每一个州都准备在完全同步通行成功之前完成,然后不产生结果。
    /// </summary>
    /// <remarks>
    /// 准备为每一个在写作中联合起来的来电者提供服务, 所以只有关闭才能取消它。
    /// </remarks>
    /// <exception cref="StatePreparationFailedException">州立的准备失败了</exception>
    private async ValueTask PrepareStatesAsync()
    {
        try
        {
            while (!TryPrepareStates(_shutdownCancellation.Token, out var preparation))
            {
                await preparation.ConfigureAwait(true);
            }

            _shutdownCancellation.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !_shutdownCancellation.IsCancellationRequested)
        {
            throw new StatePreparationFailedException(exception);
        }
    }

    /// <summary>
    /// 工作循环报告,在写作失败中,没有栅栏,就指出了一个准备失败。
    /// </summary>
    private sealed class StatePreparationFailedException(Exception innerException) : Exception(message: null, innerException);

    /// <summary>
    /// 以同步通行证准备每个州, 或者返回第一个还没有完成的准备工作。
    /// </summary>
    /// <remarks>
    /// 抓取跟随完全同步的通过而没有产生结果。 其他转弯可以使变化发生, 而不完全
    /// 准备等待准备,所以呼叫者然后再次为每一个州做好准备;一个具备先决条件的国家
    /// 同步完成,让通行证会合
    /// </remarks>
    private bool TryPrepareStates(CancellationToken cancellationToken, out ValueTask preparation)
    {
        lock (_lock)
        {
            foreach (var state in _states.Values)
            {
                preparation = state.PrepareWriteAsync(cancellationToken);
                if (!preparation.IsCompletedSuccessfully) return false;
                preparation.GetAwaiter().GetResult();
            }
        }

        preparation = default;
        return true;
    }

    private void Fence(Exception exception)
    {
        lock (_lock)
        {
            if (_state is ManagerState.Fenced)
            {
                return;
            }

            _state = ManagerState.Fenced;
            _failure = exception;
        }

        try
        {
            if (!_shutdownCancellation.IsCancellationRequested)
            {
                LogErrorProcessingWorkItems(_shared.Logger, exception);
                _grainContext?.Deactivate(new DeactivationReason(
                    DeactivationReasonCode.ApplicationError, exception, "Journaled state operation failed."));
            }
        }
        finally
        {
            // 只有在关闭取消操作时, 监视器才能完全完整。 任何其他故障都会留下变化的不确定性 。
            CompleteObservers(exception is OperationCanceledException && _shutdownCancellation.IsCancellationRequested ? null : exception);
            lock (_lock)
            {
                FaultQueuedWorkItemsUnderLock(exception);
            }
        }
    }

    private void FaultQueuedWorkItemsUnderLock(Exception exception)
    {
        while (_workQueue.TryDequeue(out var workItem))
        {
            workItem.TrySetException(exception);
        }
    }

    private void RetireOrResurectStates()
    {
        foreach (var (name, timestamp) in _retirementTracker)
        {
            var isDuetime = _shared.TimeProvider.GetUtcNow().UtcDateTime - timestamp >= _shared.RetirementGracePeriod;
            if (isDuetime && _journalStreamDirectory.TryGetValue(name, out var id))
            {
                var state = _states[name];

                Debug.Assert(state is not null);

                if (state is RetiredStateMachine)
                {
                    LogRemovingRetiredState(_shared.Logger, name);

                    // 既然我们永久取消这个状态, 我们将重新设置它来清理它。
                    state.Reset(CreateJournalStreamWriter(new(id)));

                    _statesMap.Remove(id);
                    // 我们只将这些记忆从记忆中去除,因为画面将维持这些变化。
                    _journalStreamDirectory.ApplyRemove(name);
                    _retirementTracker.ApplyRemove(name);
                }
                else
                {
                    LogRetiredStateComebackDetected(_shared.Logger, name);
                    // 我们只从记忆中移除追踪器, 因为光景将维持变化。
                    _retirementTracker.ApplyRemove(name);
                }
            }
        }
    }

    private void ThrowIfMigrationBlockedByRetiredStates()
    {
        foreach (var state in _statesMap.Values)
        {
            if (state is RetiredStateMachine { PreservedEntries.Count: > 0 } retiredState)
            {
                throw new InvalidOperationException(
                    $"Cannot migrate journal to format key '{_shared.JournalFormatKey}' because stream " +
                    $"{retiredState.StreamId.Value} belongs to a state which is not currently registered. Register the state so it can be decoded " +
                    "and snapshotted in the configured format, or keep the previous journal format configured until the state can be retired.");
            }
        }
    }

    private JournalStreamWriter CreateJournalStreamWriter(JournalStreamId streamId) => _journalWriter.CreateJournalStreamWriter(streamId);

    private static void AppendUpdatesOrSnapshotState(JournalBufferWriter journalWriter, bool isSnapshot, uint id, IStateMachine state)
    {
        var writer = journalWriter.CreateJournalStreamWriter(new(id));
        if (isSnapshot)
        {
            state.WriteSnapshot(writer);
        }
        else
        {
            state.WritePendingEntries(writer);
        }
    }

    public async ValueTask DeleteStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task task;
        bool didEnqueue;
        lock (_lock)
        {
            ThrowIfStateOperationsUnavailable();
            task = EnqueueOrGetPendingWorkItem<DeleteStateWorkItem>(out didEnqueue);
        }

        if (didEnqueue)
        {
            _workSignal.Signal();
        }

        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await task.WaitAsync(cancellationToken);
            _shared.Instruments.OnStateDeleteRequest(_shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStateDeleteRequest(_shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: false);
            throw;
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        lock (_lock)
        {
            ResetForRecovery();
        }

        try
        {
            await ReadStorageAsync(this, cancellationToken).ConfigureAwait(true);
            _shared.Instruments.OnRecovery(_shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnRecovery(_shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: false);
            throw;
        }

        lock (_lock)
        {
            foreach (var (name, state) in _states)
            {
                if (state is not RetiredStateMachine && !_journalStreamDirectory.ContainsKey(name))
                {
                    var id = _journalStreamDirectory.GetNextJournalStreamId();
                    while (_statesMap.ContainsKey(id))
                    {
                        id = checked(id + 1);
                    }

                    _journalStreamDirectory.Set(name, id);
                }
            }

            foreach ((var name, var state) in _states)
            {
                state.OnRecoveryCompleted();

                if (state is RetiredStateMachine)
                {
                    // 恢复完成后,我们可以用 TryAdd 。
                    if (_retirementTracker.TryAdd(name, _shared.TimeProvider.GetUtcNow().UtcDateTime))
                    {
                        LogRetiredStateDetected(_shared.Logger, name);
                    }
                }
            }
        }
    }

    private void ResetForRecovery()
    {
        _journalWriter.Reset();
        _migrationSnapshotRequired = false;
        _statesMap.Clear();
        _statesMap[StateDirectory.Id] = _journalStreamDirectory;
        _statesMap[RetiredStateTracker.Id] = _retirementTracker;

        List<string>? retiredNames = null;
        foreach (var (name, state) in _states)
        {
            if (state is RetiredStateMachine)
            {
                (retiredNames ??= []).Add(name);
            }
        }

        if (retiredNames is not null)
        {
            foreach (var name in retiredNames)
            {
                _states.Remove(name);
            }
        }

        _journalStreamDirectory.ResetVolatileState();
        _retirementTracker.ResetVolatileState();
    }

    internal void BindStateForReplay(JournalStreamId streamId, IStateMachine state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lock)
        {
            _statesMap[streamId.Value] = state;
            state.Reset(CreateJournalStreamWriter(streamId));
        }
    }

    internal IStateMachine ResolveStateMachine(JournalStreamId streamId)
    {
        if (!_statesMap.TryGetValue(streamId.Value, out var state))
        {
            state = new RetiredStateMachine(streamId);
            _statesMap[streamId.Value] = state;
        }

        return state;
    }

    private IJournalFormat GetJournalFormat(string journalFormatKey)
    {
        if (string.Equals(journalFormatKey, _shared.JournalFormatKey, StringComparison.Ordinal))
        {
            return _shared.JournalFormat;
        }

        return JournalFormatServices.GetRequiredJournalFormat(ServiceProvider, journalFormatKey);
    }

    private void ProcessRecoveryBuffer(JournalBufferReader buffer, IJournalMetadata? metadata)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var journalFormatKey = metadata?.FormatKey is { } storedFormatKey
            ? JournalFormatServices.ValidateJournalFormatKey(storedFormatKey)
            : _shared.JournalFormatKey;
        try
        {
            if (!string.Equals(journalFormatKey, _shared.JournalFormatKey, StringComparison.Ordinal))
            {
                _migrationSnapshotRequired = true;
            }

            var journalFormat = GetJournalFormat(journalFormatKey);
            var replayContext = new JournalReplayContext(this);
            journalFormat.Replay(buffer, replayContext);

            if (buffer.IsCompleted && buffer.Length > 0)
            {
                throw new InvalidOperationException("The journal format did not read the completed journal data.");
            }
        }
        catch (Exception exception) when (ShouldWrapRecoveryFormatException(exception))
        {
            throw CreateRecoveryFormatException(exception, journalFormatKey);
        }
    }

    void IJournalStorageConsumer.Read(JournalBufferReader buffer, IJournalMetadata? metadata) => ProcessRecoveryBuffer(buffer, metadata);

    private static bool ShouldWrapRecoveryFormatException(Exception exception) =>
        exception is not OperationCanceledException && !IsRecoveryFormatException(exception);

    private static bool IsRecoveryFormatException(Exception exception) =>
        exception is InvalidOperationException { InnerException: not null }
        && exception.Message?.StartsWith("Failed to recover journaling state using journal format key ", StringComparison.Ordinal) == true;

    private InvalidOperationException CreateRecoveryFormatException(Exception exception, string journalFormatKey) =>
        new(
            $"Failed to recover journaling state using journal format key '{journalFormatKey}'. " +
            $"The configured write journal format key is '{_shared.JournalFormatKey}'.",
            exception);

    public async ValueTask WriteStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task pendingWrite;
        bool didEnqueue;
        string operation;
        lock (_lock)
        {
            ThrowIfStateOperationsUnavailable();
            var isSnapshot = _migrationSnapshotRequired || _storage.IsCompactionRequested;
            operation = isSnapshot ? JournalingInstruments.OperationSnapshot : JournalingInstruments.OperationAppend;
            pendingWrite = isSnapshot
                ? EnqueueOrGetPendingWorkItem<WriteSnapshotWorkItem>(out didEnqueue)
                : EnqueueOrGetPendingWorkItem<AppendJournalWorkItem>(out didEnqueue);
        }

        if (didEnqueue)
        {
            _workSignal.Signal();
        }

        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await pendingWrite.WaitAsync(cancellationToken);
            _shared.Instruments.OnStateWriteRequest(operation, _shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStateWriteRequest(operation, _shared.TimeProvider.GetElapsedTime(startTimestamp), succeeded: false);
            throw;
        }
    }

    private void ThrowIfStateOperationsUnavailable()
    {
        _shutdownCancellation.Token.ThrowIfCancellationRequested();
        ThrowIfFenced();
        if (_state is not ManagerState.Ready)
        {
            throw new InvalidOperationException("The journaled state manager has not been initialized.");
        }
    }

    private void ThrowIfFenced()
    {
        if (_state is ManagerState.Fenced)
        {
            throw new InvalidOperationException(
                "Journaled state operations are fenced because an operation failed. Recover using a new grain activation or dispose this manager and create a new instance.",
                _failure);
        }
    }

    private async ValueTask ReadStorageAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await _storage.ReadAsync(consumer, cancellationToken).ConfigureAwait(true);
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationRead, _shared.TimeProvider.GetElapsedTime(startTimestamp), bytes: 0, succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationRead, _shared.TimeProvider.GetElapsedTime(startTimestamp), bytes: 0, succeeded: false);
            throw;
        }
    }

    private async ValueTask AppendStorageAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await _storage.AppendAsync(value, cancellationToken).ConfigureAwait(true);
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationAppend, _shared.TimeProvider.GetElapsedTime(startTimestamp), value.Length, succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationAppend, _shared.TimeProvider.GetElapsedTime(startTimestamp), value.Length, succeeded: false);
            throw;
        }
    }

    private async ValueTask ReplaceStorageAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await _storage.ReplaceAsync(value, cancellationToken).ConfigureAwait(true);
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationReplace, _shared.TimeProvider.GetElapsedTime(startTimestamp), value.Length, succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationReplace, _shared.TimeProvider.GetElapsedTime(startTimestamp), value.Length, succeeded: false);
            throw;
        }
    }

    private async ValueTask DeleteStorageAsync(CancellationToken cancellationToken)
    {
        var startTimestamp = _shared.TimeProvider.GetTimestamp();
        try
        {
            await _storage.DeleteAsync(cancellationToken).ConfigureAwait(true);
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationDelete, _shared.TimeProvider.GetElapsedTime(startTimestamp), bytes: 0, succeeded: true);
        }
        catch
        {
            _shared.Instruments.OnStorageOperation(JournalingInstruments.OperationDelete, _shared.TimeProvider.GetElapsedTime(startTimestamp), bytes: 0, succeeded: false);
            throw;
        }
    }

    private Task EnqueueOrGetPendingWorkItem<TWorkItem>(out bool didEnqueue)
        where TWorkItem : WorkItem, new()
    {
        foreach (var workItem in _workQueue)
        {
            if (workItem.GetType() != typeof(TWorkItem))
            {
                continue;
            }

            if (workItem is AppendJournalWorkItem or WriteSnapshotWorkItem
                && workItem.DeletionGeneration != _deletionGeneration)
            {
                continue;
            }

            workItem.RecordTraceContext();
            workItem.AddCaller();
            didEnqueue = false;
            return workItem.Task;
        }

        var newWorkItem = new TWorkItem();
        newWorkItem.EnqueuedTimestamp = _shared.TimeProvider.GetTimestamp();
        newWorkItem.DeletionGeneration = _deletionGeneration;
        newWorkItem.RecordTraceContext();
        _workQueue.Enqueue(newWorkItem);
        didEnqueue = true;
        return newWorkItem.Task;
    }

    private void BindState(string name, uint id)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(name, out var state))
            {
                _statesMap[id] = state;
                state.Reset(CreateJournalStreamWriter(new(id)));
            }
            else
            {
                var vessel = new RetiredStateMachine(new(id));

                // 我们绝不能让船在经理人面前自我登记,因为船会
                // 导致经理“ 已经准备好” 后登记延迟。 我们在此添加此内容 。

                _states.Add(name, vessel);
                _statesMap[id] = vessel;
            }
        }
    }

    public long PendingWriteByteCount => _journalWriter.CommittedLength;


    void ILifecycleParticipant<IGrainLifecycle>.Participate(IGrainLifecycle observer)
        => observer.Subscribe(GrainLifecycleStage.SetupState, this);

    Task ILifecycleObserver.OnStart(CancellationToken cancellationToken) => InitializeAsync(cancellationToken).AsTask();
    async Task ILifecycleObserver.OnStop(CancellationToken cancellationToken) => await StopAsync(cancellationToken).ConfigureAwait(false);

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _shutdownCancellation.Cancel();
        }

        _workSignal.Signal();
        try
        {
            if (_workLoop is { } task)
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        finally
        {
            CancelQueuedWorkItems(_shutdownCancellation.Token);
            CompleteObservers(failure: null);
        }
    }

    private void CancelQueuedWorkItems(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            while (_workQueue.TryDequeue(out var workItem))
            {
                workItem.TrySetCanceled(cancellationToken);
            }
        }
    }

    void IDisposable.Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _shutdownCancellation.Dispose();
            _journalWriter.Dispose();
        }
    }

    private abstract class WorkItem : TaskCompletionSource
    {
        private List<ActivityLink>? _links;

        public long EnqueuedTimestamp { get; set; }

        public long DeletionGeneration { get; set; }

        public ActivityContext TraceParent { get; private set; }

        public IReadOnlyList<ActivityLink>? TraceLinks => _links;

        /// <summary>
        /// 获取加入此工作项目(包括
        /// 最初排队的电话人 被控制锁下变异
        /// </summary>
        public int CallerCount { get; private set; } = 1;

        protected WorkItem() : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }

        protected WorkItem(object? context) : base(context, TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }

        /// <summary>
        /// 当新来电者与这个待处理的工作项目发生重叠时,会增加联名呼叫者的计数。
        /// </summary>
        public void AddCaller()
        {
            CallerCount++;
        }

        public void RecordTraceContext()
        {
            var current = Activity.Current;
            if (current is null || current.IdFormat != ActivityIdFormat.W3C)
            {
                return;
            }

            var context = current.Context;
            if (TraceParent == default)
            {
                TraceParent = context;
                return;
            }

            if (context == TraceParent)
            {
                return;
            }

            const int MaxLinks = 64;
            if (_links is null)
            {
                _links = new List<ActivityLink>();
            }
            else if (_links.Count >= MaxLinks)
            {
                return;
            }
            else
            {
                for (var i = 0; i < _links.Count; i++)
                {
                    if (_links[i].Context == context)
                    {
                        return;
                    }
                }
            }

            _links.Add(new ActivityLink(context));
        }
    }

    private static string? GetStorageQueueOperation(WorkItem workItem) =>
        workItem switch
        {
            AppendJournalWorkItem => JournalingInstruments.OperationAppend,
            WriteSnapshotWorkItem => JournalingInstruments.OperationSnapshot,
            DeleteStateWorkItem => JournalingInstruments.OperationDelete,
            _ => null
        };

    private sealed class InitializeWorkItem : WorkItem;

    private sealed class AppendJournalWorkItem : WorkItem;

    private sealed class WriteSnapshotWorkItem : WorkItem;

    private sealed class DeleteStateWorkItem : WorkItem;

    private sealed class RegisterStateWorkItem(string name) : WorkItem(name)
    {
        public string Name => (string)Task.AsyncState!;
    }

    private enum ManagerState : byte
    {
        Unknown,
        Ready,
        Fenced
    }

    private sealed class StateDirectory(
        JournaledStateManager manager,
        IDurableDictionaryCommandCodec<string, uint> codec) : IStateMachine, IDurableDictionaryCommandHandler<string, uint>
    {
        public const uint Id = 0u;

        private readonly JournaledStateManager _manager = manager;
        private readonly IDurableDictionaryCommandCodec<string, uint> _codec = codec;
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private JournalStreamWriter _writer;

        public uint this[string name] => _ids[name];

        void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
            context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

        public bool ContainsKey(string name) => _ids.ContainsKey(name);

        public bool TryGetValue(string name, out uint id) => _ids.TryGetValue(name, out id);

        public uint GetNextJournalStreamId()
        {
            var maxId = MinApplicationJournalStreamId - 1u;
            foreach (var id in _ids.Values)
            {
                if (id > maxId)
                {
                    maxId = id;
                }
            }

            return checked(maxId + 1);
        }

        public void Set(string name, uint id)
        {
            _codec.WriteSet(name, id, GetWriter());
            ApplySet(name, id);
        }

        public bool ApplyRemove(string name) => _ids.Remove(name);

        public void ResetVolatileState() => ((IStateMachine)this).Reset(_manager.CreateJournalStreamWriter(new(Id)));

        void IStateMachine.Reset(JournalStreamWriter writer)
        {
            _ids.Clear();
            _writer = writer;
        }

        void IStateMachine.WritePendingEntries(JournalStreamWriter writer) { }

        void IStateMachine.WriteSnapshot(JournalStreamWriter writer) => _codec.WriteSnapshot(_ids, writer);


        void IDurableDictionaryCommandHandler<string, uint>.ApplySet(string key, uint value) => ApplySet(key, value);

        void IDurableDictionaryCommandHandler<string, uint>.ApplyRemove(string key) => ApplyRemove(key);

        void IDurableDictionaryCommandHandler<string, uint>.ApplyClear() => _ids.Clear();

        void IDurableDictionaryCommandHandler<string, uint>.Reset(int capacityHint)
        {
            _ids.Clear();
            _ids.EnsureCapacity(capacityHint);
        }

        private void ApplySet(string name, uint id)
        {
            _ids[name] = id;
            _manager.BindState(name, id);
        }

        private JournalStreamWriter GetWriter()
        {
            Debug.Assert(_writer.IsInitialized);
            return _writer;
        }
    }

    /// <summary>
    /// 用于跟踪那些不再通过用户代码登记的国家,直到基于时间的清理工作结束。
    /// </summary>
    /// <remarks>支持退休国家重新恢复。</remarks>
    private sealed class RetiredStateTracker(
        JournaledStateManager manager, IDurableDictionaryCommandCodec<string, DateTime> codec)
            : DurableDictionary<string, DateTime>(codec)
    {
        public const uint Id = 1u;

        private readonly JournalStreamWriter _journalWriter = manager.CreateJournalStreamWriter(new(Id));

        public void ResetVolatileState() => ((IStateMachine)this).Reset(_journalWriter);

        protected override JournalStreamWriter GetWriter() => _journalWriter;
    }

    /// <summary>
    /// 用来将退休国家保留在炼狱状态,直到根据时间进行的清洗或出现回潮。
    /// 这会保持缓冲进入,然后把它们扔回压缩时的日记。
    /// </summary>
    [DebuggerDisplay("RetiredStateMachine Id = {StreamId.Value}")]
    private sealed class RetiredStateMachine(JournalStreamId streamId) : IStateMachine
    {
        private readonly List<IPreservedJournalEntry> _preservedEntries = [];

        public JournalStreamId StreamId { get; } = streamId;

        public IReadOnlyList<IPreservedJournalEntry> PreservedEntries => _preservedEntries;

        void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
            _preservedEntries.Add(new PreservedJournalEntry(entry.FormatKey, entry.Reader));

        void IStateMachine.WriteSnapshot(JournalStreamWriter snapshotWriter)
        {
            foreach (var entry in _preservedEntries)
            {
                snapshotWriter.AppendPreservedEntry(entry);
            }
        }

        void IStateMachine.Reset(JournalStreamWriter writer) => _preservedEntries.Clear();
        void IStateMachine.WritePendingEntries(JournalStreamWriter writer) { }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error processing work items.")]
    private static partial void LogErrorProcessingWorkItems(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Journal state preparation failed; pending changes remain staged.")]
    private static partial void LogStatePreparationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "State \"{Name}\" was not found. I have substituted a placeholder for graceful time-based retirement.")]
    private static partial void LogRetiredStateDetected(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "State \"{Name}\" was previously retired (but not removed), and has hence been re-introduced. " +
                  "There is still time left before its permanent removal, so I will resurrect it.")]
    private static partial void LogRetiredStateComebackDetected(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Removing retired state \"{Name}\" and its data. Operation will be durably persisted shortly after compaction has finalized.")]
    private static partial void LogRemovingRetiredState(ILogger logger, string name);
}
