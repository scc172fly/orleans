using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Diagnostics;
using Orleans.Durable.Journaling;
using Orleans.Runtime;
using Orleans.Runtime.Internal;

namespace Orleans.Durable.Jobs;

/// <summary>
/// <see cref="IJobShard"/> 的日志化实现，将分片状态存储在 Orleans 日志存储中。
/// </summary>
internal sealed class JournaledJobShard : IJobShard
{
    private readonly JournaledJobShardState _state;
    private readonly IJournaledStateManager _stateManager;
    private readonly JournaledJobShardManager _shardManager;
    private readonly DurableJobsInstruments _durableJobsInstruments;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _batchLingerDelay;
    private readonly object _pendingOperationsLock = new();
    private readonly Queue<PendingOperation> _pendingOperations = new();
    private readonly SingleWaiterAutoResetEvent _pendingOperationSignal = new() { RunContinuationsAsynchronously = true };
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly Task _operationProcessor;
    private bool _retiring;
    private int _disposed;

    /// <summary>
    /// 初始化 <see cref="JournaledJobShard"/> 类的新实例。
    /// </summary>
    /// <param name="shardId">此作业分片的唯一标识符。</param>
    /// <param name="provider">包含此分片的日志提供程序。</param>
    /// <param name="startTime">此分片管理的时间范围的开始时间。</param>
    /// <param name="endTime">此分片管理的时间范围的结束时间。</param>
    /// <param name="metadata">与此作业分片关联的可选元数据。</param>
    /// <param name="isClosed">指示此分片是否已停止接受新作业的值。</param>
    /// <param name="state">日志化分片状态。</param>
    /// <param name="stateManager">用于持久化日志化状态的管理器。</param>
    /// <param name="shardManager">拥有此分片的分片管理器。</param>
    /// <param name="timeProvider">用于批处理额外等待时间的时间提供程序。默认为 <see cref="TimeProvider.System"/>。</param>
    /// <param name="batchLingerDelay">
    /// 收到第一项变更之后，操作处理器等待其他变更加入批次的
    /// 可选时长。使用 <see cref="TimeSpan.Zero"/>（默认值）禁用额外等待。
    /// </param>
    /// <param name="durableJobsInstruments">持久化作业指标工具。</param>
    public JournaledJobShard(
        JobShardId shardId,
        DurableJobsJournalProvider provider,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        IReadOnlyDictionary<string, string>? metadata,
        bool isClosed,
        JournaledJobShardState state,
        IJournaledStateManager stateManager,
        JournaledJobShardManager shardManager,
        TimeProvider? timeProvider = null,
        TimeSpan batchLingerDelay = default,
        DurableJobsInstruments? durableJobsInstruments = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(stateManager);
        ArgumentNullException.ThrowIfNull(shardManager);
        if (batchLingerDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(batchLingerDelay), batchLingerDelay, "Batch linger delay must be non-negative.");
        }

        Id = shardId.Value;
        Provider = provider;
        StartTime = startTime;
        EndTime = endTime;
        Metadata = metadata is { Count: > 0 } ? new Dictionary<string, string>(metadata, StringComparer.Ordinal) : null;
        _state = state;
        _stateManager = stateManager;
        _shardManager = shardManager;
        _durableJobsInstruments = durableJobsInstruments ?? DurableJobsInstruments.CreateForDirectConstruction();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _batchLingerDelay = batchLingerDelay;

        if (isClosed)
        {
            _state.MarkAsComplete();
        }

        // 处理器的生命周期长于创建它的调用方，因此运行时不携带创建方的执行上下文和异步本地变量。
        using var suppressExecutionContext = new ExecutionContextSuppressor();
        _operationProcessor = Task.Run(ProcessOperationsAsync);
    }

    /// <inheritdoc/>
    public string Id { get; }

    internal DurableJobsJournalProvider Provider { get; }

    /// <inheritdoc/>
    public DateTimeOffset StartTime { get; }

    /// <inheritdoc/>
    public DateTimeOffset EndTime { get; }

    /// <inheritdoc/>
    public IDictionary<string, string>? Metadata { get; }

    /// <inheritdoc/>
    public bool IsAddingCompleted => _state.IsAddingCompleted;

    /// <summary>
    /// 获取此分片对应的底层日志标识符。
    /// </summary>
    internal JournalId StorageId => JobShardId.Parse(Id).ToJournalId();

    internal bool IsRetired => Volatile.Read(ref _retiring) || Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc/>
    public IAsyncEnumerable<IJobRunContext> ConsumeDurableJobsAsync() => _state.ConsumeDurableJobsAsync();

    /// <inheritdoc/>
    public ValueTask<int> GetJobCountAsync() => ValueTask.FromResult(_state.Count);

    /// <inheritdoc/>
    public async Task MarkAsCompleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var operation = new MarkAsCompleteOperation(cancellationToken);
        EnqueueOperation(operation);
        await operation.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭变更操作的准入，并在清理所有权之前处理完已接受的操作。
    /// </summary>
    /// <param name="cancellationToken">用于取消对完成屏障的等待的令牌。</param>
    internal async Task RetireAsync(CancellationToken cancellationToken)
    {
        using var operation = new MarkAsCompleteOperation(cancellationToken);
        lock (_pendingOperationsLock)
        {
            ThrowIfDisposed();
            _retiring = true;
            _pendingOperations.Enqueue(operation);
            _pendingOperationSignal.Signal();
        }

        // 取消等待可使注销操作释放分片并取消由分片负责的关闭 I/O。
        await operation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_state.IsAddingCompleted)
        {
            throw new InvalidOperationException($"Cannot retire DurableJobs shard '{Id}' because closing it failed.");
        }
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> TryStartAttemptAsync(
        IJobRunContext jobContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        ThrowIfDisposed();

        using var operation = new StartAttemptOperation(jobContext.Job.Id, cancellationToken);
        EnqueueOperation(operation);
        return await operation.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RemoveJobAsync(string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ThrowIfDisposed();

        using var operation = new RemoveJobOperation(jobId, cancellationToken);
        EnqueueOperation(operation);
        return await operation.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RetryJobLaterAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        ThrowIfDisposed();

        using var operation = new RetryJobLaterOperation(jobContext, newDueTime, resetDequeueCount: false, cancellationToken);
        EnqueueOperation(operation);
        return await operation.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RescheduleJobAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        ThrowIfDisposed();

        using var operation = new RetryJobLaterOperation(
            jobContext,
            newDueTime,
            resetDequeueCount: true,
            cancellationToken);
        EnqueueOperation(operation);
        return await operation.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DurableJob?> TryScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        ThrowIfDisposed();

        using var operation = new ScheduleJobOperation(request, cancellationToken);
        EnqueueOperation(operation);
        return await operation.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 删除此分片的日志化状态。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    internal async ValueTask DeleteStateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var operation = new DeleteStateOperation(cancellationToken);
        EnqueueOperation(operation);
        await operation.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            try
            {
#pragma warning disable CA1849 // 在排空自身负责的工作前，取消回调在发起关闭的调用方上运行。
                _shutdownCancellation.Cancel();
#pragma warning restore CA1849
            }
            finally
            {
                _pendingOperationSignal.Signal();
                try
                {
                    await _operationProcessor.ConfigureAwait(false);
                }
                finally
                {
                    await _stateManager.DisposeAsync();
                }
            }
        }
        finally
        {
            _shutdownCancellation.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    private void EnqueueOperation(PendingOperation operation)
    {
        operation.CancellationToken.ThrowIfCancellationRequested();

        lock (_pendingOperationsLock)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(_retiring && operation is not DeleteStateOperation, this);
            _pendingOperations.Enqueue(operation);
            _pendingOperationSignal.Signal();
        }
    }

    private async Task ProcessOperationsAsync()
    {
        var batch = new List<PendingMutationOperation>();
        try
        {
            while (true)
            {
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // 提交方法通过包围完成任务的 using 作用域拥有已排队的操作。
                if (!TryDequeueOperation(out var operation) || operation is null)
#pragma warning restore CA2000
                {
                    // 队列是权威依据：在等待下一个合并信号之前先处理完其中的操作。
                    await _pendingOperationSignal.WaitAsync().ConfigureAwait(false);
                    continue;
                }

                if (operation is PendingMutationOperation mutation)
                {
                    batch.Add(mutation);
                    DequeueConsecutiveMutations(batch);
                    if (_batchLingerDelay > TimeSpan.Zero)
                    {
                        await LingerForMoreMutationsAsync(batch).ConfigureAwait(false);
                        DequeueConsecutiveMutations(batch);
                    }
                    _durableJobsInstruments.OnShardPendingDepth(batch.Count);
                    await ProcessMutationBatchAsync(batch).ConfigureAwait(false);
                    batch.Clear();
                }
                else
                {
                    await ProcessBarrierOperationAsync(operation).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            CancelOperations(batch);
            CancelQueuedOperations();
        }
    }

    private async Task LingerForMoreMutationsAsync(List<PendingMutationOperation> batch)
    {
        try
        {
            await Task.Delay(_batchLingerDelay, _timeProvider, _shutdownCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
            return;
        }
    }

    private bool TryDequeueOperation(out PendingOperation? operation)
    {
        lock (_pendingOperationsLock)
        {
            return _pendingOperations.TryDequeue(out operation);
        }
    }

    private void DequeueConsecutiveMutations(List<PendingMutationOperation> batch)
    {
        lock (_pendingOperationsLock)
        {
#pragma warning disable CA2000 // 批处理借用由提交方的 using 作用域拥有的操作。
            while (_pendingOperations.TryPeek(out var operation) && operation is PendingMutationOperation mutation)
#pragma warning restore CA2000
            {
                _pendingOperations.Dequeue();
                batch.Add(mutation);
            }
        }
    }

    private void CancelQueuedOperations()
    {
        lock (_pendingOperationsLock)
        {
            while (_pendingOperations.TryDequeue(out var operation))
            {
                operation.TryCancel(_shutdownCancellation.Token);
            }
        }
    }

    private void CancelOperations(List<PendingMutationOperation> operations)
    {
        foreach (var operation in operations)
        {
            operation.TryCancel(_shutdownCancellation.Token);
        }

        operations.Clear();
    }

    private async Task ProcessMutationBatchAsync(List<PendingMutationOperation> operations)
    {
        var startedOperations = new List<PendingMutationOperation>(operations.Count);
        var appliedOperations = new List<PendingMutationOperation>(operations.Count);
        var operationsAwaitingWrite = new List<PendingMutationOperation>(operations.Count);

        try
        {
            foreach (var operation in operations)
            {
                if (!operation.TryStart())
                {
                    continue;
                }

                if (operation.TryCompleteWithoutOwnership(this))
                {
                    continue;
                }

                startedOperations.Add(operation);
            }

            if (startedOperations.Count == 0)
            {
                return;
            }

            var ownershipStartTimestamp = Stopwatch.GetTimestamp();
            bool isOwned;
            try
            {
                isOwned = await _shardManager.IsShardOwnedByLocalSiloAsync(Provider, Id, _shutdownCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _durableJobsInstruments.OnOwnershipCheck(Stopwatch.GetElapsedTime(ownershipStartTimestamp));
            }

            if (!isOwned)
            {
                foreach (var operation in startedOperations)
                {
                    operation.CompleteNotOwned();
                }

                return;
            }

            var pendingBytesBeforeApply = _stateManager.PendingWriteByteCount;

            foreach (var operation in startedOperations)
            {
                try
                {
                    var hasPendingWrite = appliedOperations.Count > 0;
                    if (operation.Apply(this, hasPendingWrite))
                    {
                        appliedOperations.Add(operation);
                        operationsAwaitingWrite.Add(operation);
                    }
                    else if (hasPendingWrite && !operation.Completion.IsCompleted)
                    {
                        operationsAwaitingWrite.Add(operation);
                    }
                }
                catch (Exception exception)
                {
                    operation.TrySetException(exception);
                }
            }

            if (appliedOperations.Count == 0)
            {
                return;
            }

            var pendingBytesAfterApply = _stateManager.PendingWriteByteCount;
            _durableJobsInstruments.OnShardBatch(appliedOperations.Count);
            if (pendingBytesBeforeApply >= 0 && pendingBytesAfterApply >= pendingBytesBeforeApply)
            {
                _durableJobsInstruments.OnShardBatchBytes(pendingBytesAfterApply - pendingBytesBeforeApply);
            }

            try
            {
                using var batchActivity = DurableJobsDiagnostics.StartPersistBatchActivity(appliedOperations, Id);
                await _stateManager.WriteStateAsync(_shutdownCancellation.Token).ConfigureAwait(false);
                _durableJobsInstruments.OnStorageBatchWritten(appliedOperations.Count, operationCanceled: false, error: false);
                batchActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                foreach (var operation in operationsAwaitingWrite)
                {
                    operation.CompleteAfterWrite();
                }
            }
            catch (OperationCanceledException exception) when (_shutdownCancellation.IsCancellationRequested)
            {
                _durableJobsInstruments.OnStorageBatchWritten(appliedOperations.Count, operationCanceled: true, error: false);
                foreach (var operation in operationsAwaitingWrite)
                {
                    operation.TrySetCanceled(exception.CancellationToken);
                }
            }
            catch (Exception exception)
            {
                _durableJobsInstruments.OnStorageBatchWritten(appliedOperations.Count, operationCanceled: false, error: true);
                foreach (var operation in operationsAwaitingWrite)
                {
                    operation.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException exception) when (_shutdownCancellation.IsCancellationRequested)
        {
            CompleteIncompleteOperations(operations, exception);
        }
        catch (Exception exception)
        {
            CompleteIncompleteOperations(operations, exception);
        }
    }

    private async Task ProcessBarrierOperationAsync(PendingOperation operation)
    {
        if (!operation.TryStart())
        {
            return;
        }

        try
        {
            switch (operation)
            {
                case MarkAsCompleteOperation markAsComplete:
                    if (!_state.IsAddingCompleted && await _shardManager.TryMarkShardClosedAsync(Provider, Id, _shutdownCancellation.Token).ConfigureAwait(false))
                    {
                        _state.MarkAsComplete();
                    }

                    markAsComplete.Complete();
                    break;
                case DeleteStateOperation deleteState:
                    using (var deleteActivity = DurableJobsDiagnostics.StartDeleteShardActivity(deleteState.CapturedContext, Id))
                    {
                        await _stateManager.DeleteStateAsync(_shutdownCancellation.Token).ConfigureAwait(false);
                        deleteActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                    }

                    deleteState.Complete();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported DurableJobs shard operation '{operation.GetType().Name}'.");
            }
        }
        catch (OperationCanceledException exception) when (_shutdownCancellation.IsCancellationRequested)
        {
            operation.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            operation.TrySetException(exception);
        }
    }

    private void CompleteIncompleteOperations(List<PendingMutationOperation> operations, Exception exception)
    {
        foreach (var operation in operations)
        {
            if (exception is OperationCanceledException cancellation && _shutdownCancellation.IsCancellationRequested)
            {
                operation.TrySetCanceled(cancellation.CancellationToken);
            }
            else
            {
                operation.TrySetException(exception);
            }
        }
    }

    private abstract class PendingOperation : IDisposable, DurableJobsDiagnostics.IHasCapturedContext
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _started;

        protected PendingOperation(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            CapturedContext = Activity.Current is { IdFormat: ActivityIdFormat.W3C } current
                ? current.Context
                : default;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(static state => ((PendingOperation)state!).TryCancel(), this);
            }
        }

        public CancellationToken CancellationToken { get; }

        public ActivityContext CapturedContext { get; }

        public abstract Task Completion { get; }

        public bool TryStart()
        {
            if (CancellationToken.IsCancellationRequested)
            {
                TryCancel();
                return false;
            }

            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return false;
            }

            return !Completion.IsCompleted;
        }

        public void TryCancel() => TryCancel(CancellationToken);

        public void TryCancel(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _started) == 0)
            {
                TrySetCanceled(cancellationToken);
            }
        }

        public abstract void TrySetCanceled(CancellationToken cancellationToken);

        public abstract void TrySetException(Exception exception);

        public void Dispose() => _cancellationRegistration.Dispose();
    }

    private abstract class PendingOperation<TResult> : PendingOperation
    {
        private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected PendingOperation(CancellationToken cancellationToken) : base(cancellationToken)
        {
        }

        public Task<TResult> Task => _completion.Task;

        public override Task Completion => _completion.Task;

        public override void TrySetCanceled(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);

        public override void TrySetException(Exception exception) => _completion.TrySetException(exception);

        protected void TrySetResult(TResult result) => _completion.TrySetResult(result);
    }

    private abstract class PendingMutationOperation : PendingOperation
    {
        protected PendingMutationOperation(CancellationToken cancellationToken) : base(cancellationToken)
        {
        }

        public virtual bool TryCompleteWithoutOwnership(JournaledJobShard shard) => false;

        public abstract void CompleteNotOwned();

        public abstract bool Apply(JournaledJobShard shard, bool deferCompletion);

        public abstract void CompleteAfterWrite();
    }

    private abstract class PendingMutationOperation<TResult> : PendingMutationOperation
    {
        private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TResult _result = default!;

        protected PendingMutationOperation(CancellationToken cancellationToken) : base(cancellationToken)
        {
        }

        public Task<TResult> Task => _completion.Task;

        public override Task Completion => _completion.Task;

        protected abstract TResult NotOwnedResult { get; }

        protected abstract bool Apply(JournaledJobShard shard, out TResult result);

        public override void TrySetCanceled(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);

        public override void TrySetException(Exception exception) => _completion.TrySetException(exception);

        public override void CompleteNotOwned() => _completion.TrySetResult(NotOwnedResult);

        public override bool Apply(JournaledJobShard shard, bool deferCompletion)
        {
            var writeRequired = Apply(shard, out _result);
            if (!writeRequired && !deferCompletion)
            {
                _completion.TrySetResult(_result);
            }

            return writeRequired;
        }

        public override void CompleteAfterWrite() => _completion.TrySetResult(_result);

        protected void TrySetResult(TResult result) => _completion.TrySetResult(result);
    }

    private sealed class ScheduleJobOperation(ScheduleJobRequest request, CancellationToken cancellationToken)
        : PendingMutationOperation<DurableJob?>(cancellationToken)
    {
        protected override DurableJob? NotOwnedResult => null;

        public override bool TryCompleteWithoutOwnership(JournaledJobShard shard)
        {
            if (!shard._state.IsAddingCompleted)
            {
                return false;
            }

            TrySetResult(null);
            return true;
        }

        protected override bool Apply(JournaledJobShard shard, out DurableJob? result)
        {
            result = shard._state.TryScheduleJob(request);
            return result is not null;
        }
    }

    private sealed class RemoveJobOperation(string jobId, CancellationToken cancellationToken)
        : PendingMutationOperation<DurableJobMutationResult>(cancellationToken)
    {
        protected override DurableJobMutationResult NotOwnedResult => DurableJobMutationResult.OwnershipLost;

        protected override bool Apply(JournaledJobShard shard, out DurableJobMutationResult result)
        {
            result = shard._state.RemoveJob(jobId)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
            return result == DurableJobMutationResult.Applied;
        }
    }

    private sealed class StartAttemptOperation(string jobId, CancellationToken cancellationToken)
        : PendingMutationOperation<DurableJobMutationResult>(cancellationToken)
    {
        protected override DurableJobMutationResult NotOwnedResult => DurableJobMutationResult.OwnershipLost;

        protected override bool Apply(JournaledJobShard shard, out DurableJobMutationResult result)
        {
            result = shard._state.ContainsJob(jobId)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
            return false;
        }
    }

    private sealed class RetryJobLaterOperation(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        bool resetDequeueCount,
        CancellationToken cancellationToken)
        : PendingMutationOperation<DurableJobMutationResult>(cancellationToken)
    {
        protected override DurableJobMutationResult NotOwnedResult => DurableJobMutationResult.OwnershipLost;

        protected override bool Apply(JournaledJobShard shard, out DurableJobMutationResult result)
        {
            result = shard._state.RetryJobLater(
                jobContext.Job.Id,
                newDueTime,
                resetDequeueCount ? 0 : jobContext.DequeueCount,
                resetDequeueCount ? checked(jobContext.Job.ExecutionGeneration + 1) : null)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
            return result == DurableJobMutationResult.Applied;
        }
    }

    private sealed class MarkAsCompleteOperation(CancellationToken cancellationToken) : PendingOperation<bool>(cancellationToken)
    {
        public void Complete() => TrySetResult(true);
    }

    private sealed class DeleteStateOperation(CancellationToken cancellationToken) : PendingOperation<bool>(cancellationToken)
    {
        public void Complete() => TrySetResult(true);
    }
}
