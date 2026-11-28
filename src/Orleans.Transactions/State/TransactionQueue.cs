using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.Diagnostics;
using Orleans.Storage;
using Orleans.Configuration;
using Orleans.Timers.Internal;

namespace Orleans.Transactions.State
{
    [SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The cancellation source remains valid for all shutdown observers and is reclaimed with the grain activation.")]
    internal partial class TransactionQueue<TState>
        where TState : class, new()
    {
        private readonly TransactionalStateOptions options;
        private readonly ParticipantId resource;
        private readonly Action deactivate;
        private readonly ITransactionalStateStorage<TState> storage;
        private readonly BatchWorker storageWorker;
        protected readonly ILogger logger;
        private readonly CancellationTokenSource onDeactivating = new();
        private readonly ConfirmationWorker<TState> confirmationWorker;
        private readonly TransactionDiagnosticEvents.TransactionDiagnosticIdentity diagnosticIdentity;
        private CommitQueue<TState> commitQueue;
        private Task readyTask;

        protected StorageBatch<TState> storageBatch = null!;

        private int failCounter;

        // 清理任务。
        private readonly Dictionary<DateTime, PreparedMessages> unprocessedPreparedMessages;
        private class PreparedMessages
        {
            public PreparedMessages(TransactionalStatus status)
            {
                this.Status = status;
            }
            public int Count;
            public TransactionalStatus Status;
        }

        private TState stableState = null!;
        private long stableSequenceNumber;
        public ReadWriteLock<TState> RWLock { get; }
        public CausalClock Clock { get; }
        internal ParticipantId Resource => resource;
        internal TimeSpan PrepareTimeout => options.PrepareTimeout;
        internal CancellationToken OnDeactivating => onDeactivating.Token;
        internal TransactionDiagnosticEvents.TransactionDiagnosticIdentity DiagnosticIdentity => diagnosticIdentity;

        public TransactionQueue(
            IOptions<TransactionalStateOptions> options,
            ParticipantId resource,
            Action deactivate,
            ITransactionalStateStorage<TState> storage,
            IClock clock,
            ILogger logger,
            ITimerManager timerManager,
            TransactionDiagnosticEvents.TransactionDiagnosticIdentity diagnosticIdentity)
        {
            this.options = options.Value;
            this.resource = resource;
            this.deactivate = deactivate;
            this.storage = storage;
            this.Clock = new CausalClock(clock);
            this.logger = logger;
            this.diagnosticIdentity = diagnosticIdentity;
            this.storageWorker = new BatchWorkerFromDelegate(StorageWork, OnDeactivating);
            this.RWLock = new ReadWriteLock<TState>(options, this, this.storageWorker, logger, OnDeactivating);
            this.confirmationWorker = new ConfirmationWorker<TState>(options, this.resource, this.storageWorker, () => this.storageBatch, this.logger, timerManager, OnDeactivating);
            this.unprocessedPreparedMessages = new Dictionary<DateTime, PreparedMessages>();
            this.commitQueue = new CommitQueue<TState>();
            this.readyTask = Task.CompletedTask;
        }

        internal Task StopAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA1849 // 关闭观察者在调用方的激活实例调度器上运行，之后才恢复排队的存储周期。
            onDeactivating.Cancel(throwOnFirstException: false);
#pragma warning restore CA1849

            // 存储周期和关闭操作均在激活实例调度器上运行。已取消的周期在访问存储前返回。
            return cancellationToken.IsCancellationRequested
                ? Task.CompletedTask
                : storageWorker.WaitForCurrentWorkToBeServiced().WaitAsync(cancellationToken);
        }

        public async Task EnqueueCommit(TransactionRecord<TState> record)
        {
            try
            {
                LogTraceStartTwoPhaseCommit(record.TransactionId, new(record.Timestamp));
                commitQueue.Add(record);

                // 每种提交类型对应的额外操作。
                switch (record.Role)
                {
                    case CommitRole.ReadOnly:
                        {
                            // 不需要额外操作。
                            break;
                        }

                    case CommitRole.LocalCommit:
                        {
                            // 处理提前收到的已准备消息。
                            if (unprocessedPreparedMessages.TryGetValue(record.Timestamp, out PreparedMessages? info))
                            {
                                if (info.Status == TransactionalStatus.Ok)
                                {
                                    record.WaitCount -= info.Count;
                                }
                                else
                                {
                                    await AbortCommits(info.Status, commitQueue.Count - 1);

                                    this.RWLock.Notify();
                                }
                                unprocessedPreparedMessages.Remove(record.Timestamp);
                            }
                            break;
                        }

                    case CommitRole.RemoteCommit:
                        {

                            // 优化：如果依赖关系已隐含确定，则可以立即继续。
                            bool behindRemoteEntryBySameTM = false;
                            /* disabled - jbragg - TODO - revisit
                            commitQueue.Count >= 2
                            && commitQueue[commitQueue.Count - 2] is TransactionRecord<TState> rce
                            && rce.Role == CommitRole.RemoteCommit
                            && rce.TransactionManager.Equals(record.TransactionManager);
                            */

                            if (record.NumberWrites > 0)
                            {
                                this.storageBatch.Prepare(record.SequenceNumber, record.TransactionId, record.Timestamp, record.TransactionManager, record.State);
                            }
                            else
                            {
                                this.storageBatch.Read(record.Timestamp);
                            }

                            this.storageBatch.FollowUpAction(success =>
                            {
                                if (!success)
                                {
                                    return;
                                }

                                LogTracePersisted(record);
                                record.PrepareIsPersisted = true;
                                TransactionDiagnosticEvents.EmitRemotePreparePersisted(
                                    resource,
                                    record.TransactionId,
                                    record.Timestamp,
                                    record.TransactionManager,
                                    identity: diagnosticIdentity);

                                if (behindRemoteEntryBySameTM)
                                {
                                    LogTraceSendingImmediatePrepared(record);
                                    // 持久化准备记录后即可立即发送已准备消息。
                                    record.TransactionManager.Reference.AsReference<ITransactionManagerExtension>()
                                          .Prepared(
                                              record.TransactionManager.Name,
                                              record.TransactionId,
                                              record.Timestamp,
                                              this.resource,
                                              TransactionalStatus.Ok,
                                              CancellationToken.None)
                                          .Ignore();
                                    record.LastSent = DateTime.UtcNow;
                                    TransactionDiagnosticEvents.EmitRemotePreparedSent(
                                        resource,
                                        record.TransactionId,
                                        record.Timestamp,
                                        record.TransactionManager,
                                        record.LastSent.Value,
                                        identity: diagnosticIdentity);
                                }
                            });
                            break;
                        }

                    default:
                        {
                            LogErrorImpossibleCase(record.Role);
                            throw new NotSupportedException($"{record.Role} is not a supported CommitRole.");
                        }
                }
            }
            catch (Exception exception)
            {
                LogErrorTransactionAbortInternalError(exception);
                await NotifyOfAbort(record, TransactionalStatus.UnknownException, exception);
            }
        }

        public async Task NotifyOfPrepared(Guid transactionId, DateTime timeStamp, ParticipantId participant, TransactionalStatus status)
        {
            var pos = commitQueue.Find(transactionId, timeStamp);
            LogTraceNotifyOfPrepared(transactionId, new(timeStamp), status);
            if (pos != -1)
            {

                var localEntry = commitQueue[pos];

                if (localEntry.Role != CommitRole.LocalCommit)
                {
                    LogErrorTransactionAbortWrongCommitType();
                    throw new InvalidOperationException($"Wrong commit type: {localEntry.Role}");
                }

                if (status == TransactionalStatus.Ok)
                {
                    localEntry.WaitCount--;
                    TransactionDiagnosticEvents.EmitPreparedReceived(
                        resource,
                        transactionId,
                        timeStamp,
                        participant,
                        status,
                        localEntry.WaitCount,
                        identity: diagnosticIdentity);

                    storageWorker.Notify();
                }
                else
                {
                    TransactionDiagnosticEvents.EmitPreparedReceived(
                        resource,
                        transactionId,
                        timeStamp,
                        participant,
                        status,
                        localEntry.WaitCount,
                        identity: diagnosticIdentity);
                    await AbortCommits(status, pos);

                    this.RWLock.Notify();
                }
            }
            else
            {
                // 此消息比提交请求先到达，需要将其记录下来。
                if (!this.unprocessedPreparedMessages.TryGetValue(timeStamp, out PreparedMessages? info))
                {
                    this.unprocessedPreparedMessages[timeStamp] = info = new PreparedMessages(status);
                }
                if (status == TransactionalStatus.Ok)
                {
                    info.Count++;
                }
                else
                {
                    info.Status = status;
                }

                TransactionDiagnosticEvents.EmitPreparedReceived(
                    resource,
                    transactionId,
                    timeStamp,
                    participant,
                    status,
                    remainingCount: null,
                    identity: diagnosticIdentity);

                // TODO：修复对应提交消息始终未到达时的内存泄漏问题。
            }
        }

        public async Task NotifyOfPrepare(Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager)
        {
            var locked = await this.RWLock.ValidateLock(transactionId, accessCount);
            var status = locked.Item1;
            var record = locked.Item2;
            var valid = status == TransactionalStatus.Ok;

            record.Timestamp = timeStamp;
            record.Role = CommitRole.RemoteCommit; // 当前参与者不是 TM。
            record.TransactionManager = transactionManager;
            record.LastSent = null;
            record.PrepareIsPersisted = false;

            if (!valid)
            {
                await this.NotifyOfAbort(record, status, exception: null);
            }
            else
            {
                this.Clock.Merge(record.Timestamp);
            }

            this.RWLock.Notify();
        }

        public async Task NotifyOfAbort(TransactionRecord<TState> entry, TransactionalStatus status, Exception? exception)
        {
            switch (entry.Role)
            {
                case CommitRole.NotYetDetermined:
                    {
                        // 无法通知任何参与者。TA 会在准备阶段检测到锁被解除。
                        break;
                    }
                case CommitRole.RemoteCommit:
                    {
                        LogTraceAborting(status, entry);

                        entry.ConfirmationResponsePromise?.TrySetException(new OrleansException($"Confirm failed: Status {status}"));

                        if (entry.LastSent.HasValue)
                            return; // 如果已发送准备成功消息，则不能再中止事务。

                        LogTraceAbortingViaPrepared(status, entry);
                        entry.TransactionManager.Reference.AsReference<ITransactionManagerExtension>()
                             .Prepared(
                                 entry.TransactionManager.Name,
                                 entry.TransactionId,
                                 entry.Timestamp,
                                 resource,
                                 status,
                                 CancellationToken.None)
                             .Ignore();
                        break;
                    }
                case CommitRole.LocalCommit:
                    {
                        LogTraceAborting(status, entry);
                        var deactivationToken = OnDeactivating;
                        var fanOutDiagnosticsEnabled = AreCancelFanOutEventsEnabled();
                        var targetCount = 0;
                        var selfTargetCount = 0;
                        var fanOutStartedAt = 0L;
                        if (fanOutDiagnosticsEnabled)
                        {
                            (targetCount, selfTargetCount) = CountCancelTargets(entry.WriteParticipants);
                            if (targetCount > 0)
                            {
                                TransactionDiagnosticEvents.EmitCancelFanOutStarted(
                                    resource,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    targetCount,
                                    selfTargetCount,
                                    diagnosticIdentity);
                                fanOutStartedAt = Stopwatch.GetTimestamp();
                            }
                        }

                        var fanOutTasks = new List<Task>();
                        foreach (var participant in entry.WriteParticipants)
                        {
                            if (participant.Equals(resource))
                            {
                                continue;
                            }

                            try
                            {
                                fanOutTasks.Add(SendCancel(
                                    participant,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    TransactionDiagnosticEvents.CancelReason.TransactionAbort));
                            }
                            catch (Exception ex)
                            {
                                fanOutTasks.Add(Task.FromException(ex));
                            }
                        }

                        var fanOut = Task.WhenAll(fanOutTasks);
                        CompleteAbortDecision(entry, status, exception);
                        var cleanupBudgetExpired = false;

                        try
                        {
                            var cleanupDeadline = Task.Delay(this.options.LockTimeout, deactivationToken);
                            var completed = await Task.WhenAny(fanOut, cleanupDeadline);
                            if (ShouldAbandonCancelFanOut(fanOut, completed))
                            {
                                fanOut.Ignore();
                                await cleanupDeadline;
                                cleanupBudgetExpired = true;
                                throw new TimeoutException(
                                    $"Cancel fan-out did not complete within the {this.options.LockTimeout} cleanup budget.");
                            }

                            await fanOut;

                            if (fanOutDiagnosticsEnabled && targetCount > 0)
                            {
                                TransactionDiagnosticEvents.EmitCancelFanOutCompleted(
                                    resource,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    targetCount,
                                    selfTargetCount,
                                    Stopwatch.GetElapsedTime(fanOutStartedAt),
                                    diagnosticIdentity);
                            }
                        }
                        catch (OperationCanceledException ex) when (deactivationToken.IsCancellationRequested)
                        {
                            fanOut.Ignore();
                            if (fanOutDiagnosticsEnabled && targetCount > 0)
                            {
                                TransactionDiagnosticEvents.EmitCancelFanOutFailed(
                                    resource,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    targetCount,
                                    selfTargetCount,
                                    Stopwatch.GetElapsedTime(fanOutStartedAt),
                                    ex,
                                    diagnosticIdentity);
                            }

                            LogWarningCancelFanOutAbandonedDuringDeactivation(
                                entry.TransactionId,
                                new(entry.Timestamp),
                                status,
                                ex);
                        }
                        catch (TimeoutException ex) when (cleanupBudgetExpired)
                        {
                            fanOut.Ignore();
                            if (fanOutDiagnosticsEnabled && targetCount > 0)
                            {
                                TransactionDiagnosticEvents.EmitCancelFanOutFailed(
                                    resource,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    targetCount,
                                    selfTargetCount,
                                    Stopwatch.GetElapsedTime(fanOutStartedAt),
                                    ex,
                                    diagnosticIdentity);
                            }

                            LogWarningCancelFanOutCleanupTimedOut(
                                entry.TransactionId,
                                new(entry.Timestamp),
                                status,
                                this.options.LockTimeout,
                                ex);
                        }
                        catch (Exception ex)
                        {
                            if (fanOutDiagnosticsEnabled && targetCount > 0)
                            {
                                TransactionDiagnosticEvents.EmitCancelFanOutFailed(
                                    resource,
                                    entry.TransactionId,
                                    entry.Timestamp,
                                    status,
                                    targetCount,
                                    selfTargetCount,
                                    Stopwatch.GetElapsedTime(fanOutStartedAt),
                                    ex,
                                    diagnosticIdentity);
                            }

                            LogWarningFailedToNotifyAllTransactionParticipantsOfCancellation(entry.TransactionId, new(entry.Timestamp), status, ex);
                        }

                        break;
                    }
                case CommitRole.ReadOnly:
                    {
                        LogTraceAborting(status, entry);
                        CompleteAbortDecision(entry, status, exception);

                        break;
                    }
                default:
                    {
                        LogErrorImpossibleCase(entry.Role);
                        throw new NotSupportedException($"{entry.Role} is not a supported CommitRole.");
                    }
            }
        }

        private void CompleteAbortDecision(
            TransactionRecord<TState> entry,
            TransactionalStatus status,
            Exception? exception)
        {
            var completed = exception is not null
                ? entry.PromiseForTA.TrySetException(exception)
                : entry.PromiseForTA.TrySetResult(status);

            if (completed)
            {
                TransactionDiagnosticEvents.EmitTransactionManagerAbortDecisionCompleted(
                    resource,
                    entry.TransactionId,
                    entry.Timestamp,
                    status,
                    diagnosticIdentity);
            }
        }

        public async Task NotifyOfPing(Guid transactionId, DateTime timeStamp, ParticipantId resource)
        {
            if (this.commitQueue.Find(transactionId, timeStamp) != -1)
            {
                // 目前无需采取特殊操作：事务仍处于
                // 提交队列中，状态尚未确定。
                // 提交或中止后将发送确认或取消消息。

                LogTraceReceivedPingIrrelevant(transactionId);
                this.storageWorker.Notify(); // 以防工作器已进入等待状态等情况。
            }
            else
            {
                if (!this.confirmationWorker.IsConfirmed(transactionId))
                {
                    LogTraceReceivedPingUnknown(transactionId);

                    // 从未收到过此事务的信息，因此它必定已中止。
                    await SendCancel(
                        resource,
                        transactionId,
                        timeStamp,
                        TransactionalStatus.PresumedAbort,
                        TransactionDiagnosticEvents.CancelReason.RecoveryPing);
                }
            }
        }

        public async Task NotifyOfConfirm(Guid transactionId, DateTime timeStamp)
        {
            LogTraceNotifyOfConfirm(transactionId, new(timeStamp));

            // 在队列中查找。
            var pos = commitQueue.Find(transactionId, timeStamp);

            if (pos == -1)
            {
                TransactionDiagnosticEvents.EmitTransactionConfirmCompleted(
                    resource,
                    transactionId,
                    timeStamp,
                    TransactionalStatus.Ok,
                    queueEntryFound: false,
                    succeeded: true,
                    identity: diagnosticIdentity);
                return; // 必定已确认。
            }

            var remoteEntry = commitQueue[pos];

            if (remoteEntry.Role != CommitRole.RemoteCommit)
            {
                LogErrorInternalErrorNotifyOfConfirmWrongCommitType();
                throw new InvalidOperationException($"Wrong commit type: {remoteEntry.Role}");
            }

            // 设置此字段使该条目准备好加入批次。

            remoteEntry.ConfirmationResponsePromise ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            storageWorker.Notify();

            // 现在等待批次完成。

            var confirmStatus = TransactionalStatus.Ok;
            try
            {
                await remoteEntry.ConfirmationResponsePromise.Task;
            }
            catch
            {
                confirmStatus = TransactionalStatus.UnknownException;
                throw;
            }
            finally
            {
                TransactionDiagnosticEvents.EmitTransactionConfirmCompleted(
                    resource,
                    transactionId,
                    timeStamp,
                    confirmStatus,
                    queueEntryFound: true,
#pragma warning disable CA1508 // 持久化失败时，catch 会在此 finally 执行前设置 UnknownException。
                    succeeded: confirmStatus == TransactionalStatus.Ok,
#pragma warning restore CA1508
                    identity: diagnosticIdentity);
            }
        }

        public async Task NotifyOfCancel(Guid transactionId, DateTime timeStamp, TransactionalStatus status)
        {
            LogTraceNotifyOfCancel(nameof(NotifyOfCancel), transactionId, new(timeStamp), status);
            // 在队列中查找。
            var pos = commitQueue.Find(transactionId, timeStamp);

            if (pos == -1)
            {
                // Cancel 可能先于单向准备消息到达。释放准备前持有的锁，使
                // 迟到的准备操作检测到锁已解除，在不持久化远程提交的情况下完成。
                this.RWLock.Rollback(transactionId);
                this.RWLock.Notify();
                TransactionDiagnosticEvents.EmitTransactionCancelCompleted(
                    resource,
                    transactionId,
                    timeStamp,
                    status,
                    queueEntryFound: false,
                    succeeded: true,
                    identity: diagnosticIdentity);
                return;
            }

            var succeeded = false;
            try
            {
                this.storageBatch.Cancel(commitQueue[pos].SequenceNumber);
                await AbortCommits(status, pos);
                storageWorker.Notify();
                this.RWLock.Notify();
                succeeded = true;
            }
            finally
            {
                TransactionDiagnosticEvents.EmitTransactionCancelCompleted(
                    resource,
                    transactionId,
                    timeStamp,
                    status,
                    queueEntryFound: true,
                    succeeded,
                    identity: diagnosticIdentity);
            }
        }

        /// <summary>
        /// 在激活时以及从存储冲突或其他异常中恢复时调用。
        /// </summary>
        public async Task NotifyOfRestore()
        {
            try
            {
                await Ready();
            }
            finally
            {
                this.readyTask = Restore();
            }
            await this.readyTask;
        }

        /// <summary>
        /// 确保队列已准备好处理请求。
        /// </summary>
        /// <returns></returns>
        public Task Ready(Guid? transactionId = null)
        {
            if (this.readyTask.IsCompletedSuccessfully)
            {
                return readyTask;
            }

            TransactionDiagnosticEvents.EmitReadyWaitStarted(resource, transactionId, diagnosticIdentity);
            return ReadyAsync();
            async Task ReadyAsync()
            {
                var recoveredAfterFailure = false;
                try
                {
                    await readyTask;
                }
                catch (Exception exception)
                {
                    recoveredAfterFailure = true;
                    TransactionDiagnosticEvents.EmitReadyWaitFailed(resource, transactionId, exception, diagnosticIdentity);
                    LogWarningExceptionInTransactionQueue(exception);
                    await AbortAndRestore(TransactionalStatus.UnknownException, exception, storageOutcomeInDoubt: false);
                }

                TransactionDiagnosticEvents.EmitReadyWaitCompleted(
                    resource,
                    transactionId,
                    recoveredAfterFailure,
                    diagnosticIdentity);
            }
        }

        private async Task Restore(ImmutableArray<Guid> transactionIds = default)
        {
            if (transactionIds.IsDefault)
            {
                transactionIds = ImmutableArray<Guid>.Empty;
            }

            TransactionDiagnosticEvents.EmitQueueRestoreStarted(resource, transactionIds, diagnosticIdentity);

            TransactionalStorageLoadResponse<TState> loadresponse;
            try
            {
                loadresponse = await storage.Load();
            }
            catch (Exception exception)
            {
                var storageConflict = exception is InconsistentStateException;
                if (storageConflict)
                {
                    TransactionDiagnosticEvents.EmitStorageConflictDetected(
                        resource,
                        TransactionDiagnosticEvents.StorageOperation.Load,
                        storageOutcomeInDoubt: false,
                        queuedTransactionCount: transactionIds.Length,
                        exception: exception,
                        transactionIds: transactionIds,
                        identity: diagnosticIdentity);
                }

                TransactionDiagnosticEvents.EmitQueueRestoreFailed(
                    resource,
                    exception,
                    storageConflict,
                    transactionIds,
                    diagnosticIdentity);
                throw;
            }

            this.storageBatch = new StorageBatch<TState>(loadresponse);

            this.stableState = loadresponse.CommittedState;
            this.stableSequenceNumber = loadresponse.CommittedSequenceId;

            LogDebugLoad(stableSequenceNumber, loadresponse.PendingStates.Count, storageBatch.MetaData.CommitRecords.Count);

            // 确保时钟与已加载的状态一致。
            this.Clock.Merge(storageBatch.MetaData.TimeStamp);

            // 恢复已准备的事务（当前参与者不是 TM）。
            var recoveredPendingCount = 0;
            foreach (var pr in loadresponse.PendingStates.OrderBy(ps => ps.TimeStamp))
            {
                if (pr.SequenceId > loadresponse.CommittedSequenceId && pr.TransactionManager.Reference != null)
                {
                    LogDebugRecoverTwoPhaseCommit(pr.TransactionId);
                    ParticipantId tm = pr.TransactionManager;
                    var transactionId = Guid.Parse(pr.TransactionId);

                    commitQueue.Add(new TransactionRecord<TState>()
                    {
                        Role = CommitRole.RemoteCommit,
                        TransactionId = transactionId,
                        Timestamp = pr.TimeStamp,
                        State = pr.State,
                        SequenceNumber = pr.SequenceId,
                        TransactionManager = tm,
                        PrepareIsPersisted = true,
                        LastSent = default(DateTime),
                        IsRestoredRemoteCommit = true,
                        ConfirmationResponsePromise = null,
                        NumberWrites = 1 // 该事务执行过写入。
                    });
                    this.stableSequenceNumber = pr.SequenceId;
                    recoveredPendingCount++;

                    TransactionDiagnosticEvents.EmitRemoteRecoveryPingScheduled(
                        resource,
                        transactionId,
                        pr.TimeStamp,
                        tm,
                        DateTime.UtcNow,
                        identity: diagnosticIdentity);
                }
            }

            // 恢复已提交事务（在 TM 上）。
            foreach (var kvp in storageBatch.MetaData.CommitRecords)
            {
                LogDebugRecoverCommitConfirmation(kvp.Key);
                this.confirmationWorker.Add(kvp.Key, kvp.Value.Timestamp, kvp.Value.WriteParticipants);
            }

            TransactionDiagnosticEvents.EmitQueueRestoreCompleted(
                resource,
                loadresponse.CommittedSequenceId,
                recoveredPendingCount,
                storageBatch.MetaData.CommitRecords.Count,
                transactionIds,
                diagnosticIdentity);

            // 检查是否有待处理工作。
            this.storageWorker.Notify();
            this.RWLock.Notify();
        }

        /// <summary>
        /// 返回最近一次提交的状态，不包括仍可能中止的提交队列条目。
        /// </summary>
        public void GetCommittedState(out TState state, out long sequenceNumber)
        {
            state = this.stableState;
            sequenceNumber = this.stableSequenceNumber;
        }

        public void GetMostRecentState(out TState state, out long sequenceNumber)
        {
            if (commitQueue.Count == 0)
            {
                state = this.stableState;
                sequenceNumber = this.stableSequenceNumber;
            }
            else
            {
                var record = commitQueue.Last;
                state = record.State;
                sequenceNumber = record.SequenceNumber;
            }
        }

        public int BatchableOperationsCount()
        {
            int count = 0;
            int pos = commitQueue.Count - 1;
            while (pos >= 0 && commitQueue[pos].Batchable)
            {
                pos--;
                count++;
            }
            return count;
        }

        private async Task StorageWork()
        {
            StorageBatch<TState>? batchBeingSentToStorage = null;
            var batchCompletedSuccessfully = false;
            var recoveryInitiated = false;

            if (!this.onDeactivating.IsCancellationRequested)
            {
                var writeAttempted = false;

                try
                {
                    // 统计提交队列底部可提交的条目数量。
                    int committableEntries = 0;
                    while (committableEntries < commitQueue.Count && commitQueue[committableEntries].ReadyToCommit)
                    {
                        committableEntries++;
                    }

                    // 处理所有可提交的条目，组装存储批次。
                    if (committableEntries > 0)
                    {
                        // 处理所有可提交的条目，将存储事件添加到存储批次中。
                        CollectEventsForBatch(committableEntries);
                        LogDebugBatchCommit(committableEntries, commitQueue.Count - committableEntries, new(commitQueue, committableEntries));
                    }
                    else
                    {
                        // 发送或重新发送消息，并检测超时。
                        await CheckProgressOfCommitQueue();
                    }

                    // 当前存储批次非空时，将其写入存储。
                    if (this.storageBatch.BatchSize > 0)
                    {
                        // 准备好下一批次，使其能够在写入旧批次期间继续填充。
                        batchBeingSentToStorage = this.storageBatch;
                        this.storageBatch = new StorageBatch<TState>(batchBeingSentToStorage);

                        try
                        {
                            if (await batchBeingSentToStorage.CheckStorePreConditions())
                            {
                                // 一旦调用 Store，失败的结果便具有不确定性：写入可能失败，也可能
                                // 已成功而仅响应失败或超时。从存储恢复以确定结果。
                                writeAttempted = true;

                                // 执行实际的存储操作，并记录 ETag。
                                this.storageBatch.ETag = await batchBeingSentToStorage.Store(storage);
                                failCounter = 0;
                            }
                            else
                            {
                                LogWarningStorePreConditionsNotMet();
                                recoveryInitiated = true;
                                await AbortAndRestore(TransactionalStatus.CommitFailure, exception: null, storageOutcomeInDoubt: false);
                                return;
                            }
                        }
                        catch (Exception exception)
                        {
                            if (recoveryInitiated)
                            {
                                throw;
                            }

                            TransactionalStatus status;
                            if (exception is InconsistentStateException)
                            {
                                status = TransactionalStatus.StorageConflict;
                                var transactionIds = TransactionDiagnosticEvents.IsEnabled(
                                    nameof(TransactionDiagnosticEvents.StorageConflictDetected))
                                    ? CaptureTransactionIds()
                                    : ImmutableArray<Guid>.Empty;
                                TransactionDiagnosticEvents.EmitStorageConflictDetected(
                                    resource,
                                    TransactionDiagnosticEvents.StorageOperation.Store,
                                    writeAttempted,
                                    commitQueue.Count,
                                    exception,
                                    transactionIds,
                                    diagnosticIdentity);
                                LogWarningReloadFromStorageTriggeredByETagMismatch(exception);
                            }
                            else
                            {
                                status = TransactionalStatus.UnknownException;
                                LogWarningStorageExceptionInStorageWorker(exception);
                            }

                            recoveryInitiated = true;
                            await AbortAndRestore(status, exception, storageOutcomeInDoubt: writeAttempted);
                            return;
                        }

                        TransactionDiagnosticEvents.EmitStorageWriteCompleted(
                            this.resource,
                            this.storageBatch.ETag,
                            batchBeingSentToStorage.BatchSize,
                            batchBeingSentToStorage.CommitCount,
                            batchBeingSentToStorage.CommittedTransactionIds,
                            diagnosticIdentity);
                    }

                    if (committableEntries > 0)
                    {
                        // 更新稳定状态。
                        var lastCommittedEntry = commitQueue[committableEntries - 1];
                        this.stableState = lastCommittedEntry.State;
                        this.stableSequenceNumber = lastCommittedEntry.SequenceNumber;
                        LogTraceStableStateVersion(stableSequenceNumber);

                        // 从提交队列中移除已提交的条目。
                        commitQueue.RemoveFromFront(committableEntries);
                        storageWorker.Notify();  // 必须重新检查是否有待处理工作。
                    }

                    if (batchBeingSentToStorage != null)
                    {
                        batchBeingSentToStorage.Complete(success: true);
                        batchCompletedSuccessfully = true;
                        storageWorker.Notify();  // 必须重新检查是否有待处理工作。
                    }
                }
                catch (Exception exception)
                {
                    LogWarningExceptionInStorageWorker(failCounter, exception);

                    if (recoveryInitiated)
                    {
                        throw;
                    }

                    // 如果已尝试写入，则持久化结果未知，必须通过恢复过程确定结果。
                    recoveryInitiated = true;
                    await AbortAndRestore(TransactionalStatus.UnknownException, exception, storageOutcomeInDoubt: writeAttempted);
                }
                finally
                {
                    if (batchBeingSentToStorage != null && !batchCompletedSuccessfully)
                    {
                        batchBeingSentToStorage.Complete(success: false);
                    }
                }
            }
        }

        private async Task AbortAndRestore(TransactionalStatus status, Exception? exception, bool storageOutcomeInDoubt)
        {
            var task = this.readyTask = AbortAndRestoreCore(status, exception, storageOutcomeInDoubt: storageOutcomeInDoubt);
            await task;

            async Task AbortAndRestoreCore(TransactionalStatus status, Exception? exception, bool storageOutcomeInDoubt)
            {
                var transactionIds = AreRecoveryCorrelationEventsEnabled()
                    ? CaptureTransactionIds()
                    : ImmutableArray<Guid>.Empty;
                TransactionDiagnosticEvents.EmitAbortAndRestoreStarted(
                    resource,
                    status,
                    storageOutcomeInDoubt,
                    commitQueue.Count,
                    transactionIds,
                    diagnosticIdentity);

                List<Task> pending =
                [
                    RWLock.AbortExecutingTransactions(
                        exception,
                        TransactionDiagnosticEvents.LockBreakReason.StorageRecovery)
                ];
                this.RWLock.AbortQueuedTransactions();

                foreach (var entry in commitQueue.Elements)
                {
                    if (storageOutcomeInDoubt)
                    {
                        // 异常出现前存储写入可能已经提交，或者写入完成后某项后续操作
                        // 可能失败。不要根据过时的
                        // 内存状态发送中止或取消消息；应从存储恢复，由恢复过程确认或中止事务。
                        CompleteInDoubtEntryLocally(entry, status, exception);
                    }
                    else
                    {
                        pending.Add(NotifyOfAbort(entry, status, exception: exception));
                    }
                }

                commitQueue.Clear();

                await Task.WhenAll(pending);
                var failureCount = ++failCounter;
                if (failureCount >= 10 || status == TransactionalStatus.StorageConflict)
                {
                    LogDebugStorageWorkerTriggeringGrainDeactivation();
                    TransactionDiagnosticEvents.EmitDeactivationRequested(
                        resource,
                        status,
                        failureCount,
                        transactionIds,
                        diagnosticIdentity);
                    this.deactivate();
                }
                StorageBatch<TState>? discardedBatch = this.storageBatch;
                await this.Restore(transactionIds);
                if (discardedBatch is not null && !ReferenceEquals(discardedBatch, this.storageBatch))
                {
                    discardedBatch.Complete(success: false);
                }
                TransactionDiagnosticEvents.EmitAbortAndRestoreCompleted(
                    resource,
                    status,
                    storageOutcomeInDoubt,
                    transactionIds,
                    diagnosticIdentity);
            }
        }

        private static bool AreRecoveryCorrelationEventsEnabled()
            => TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.AbortAndRestoreStarted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.AbortAndRestoreCompleted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.QueueRestoreStarted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.QueueRestoreCompleted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.QueueRestoreFailed))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.StorageConflictDetected))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.DeactivationRequested));

        private ImmutableArray<Guid> CaptureTransactionIds()
        {
            if (commitQueue.Count == 0)
            {
                return ImmutableArray<Guid>.Empty;
            }

            var result = ImmutableArray.CreateBuilder<Guid>(commitQueue.Count);
            for (var i = 0; i < commitQueue.Count; i++)
            {
                result.Add(commitQueue[i].TransactionId);
            }

            return result.MoveToImmutable();
        }

        protected virtual async Task SendCancel(
            ParticipantId target,
            Guid transactionId,
            DateTime timeStamp,
            TransactionalStatus status,
            TransactionDiagnosticEvents.CancelReason reason)
        {
            var isSelf = target.Reference.GrainId == resource.Reference.GrainId;
            TransactionDiagnosticEvents.EmitCancelSendStarted(
                resource,
                transactionId,
                timeStamp,
                target,
                isSelf,
                status,
                reason,
                diagnosticIdentity);

            try
            {
                await target.Reference.AsReference<ITransactionalResourceExtension>()
                    .Cancel(target.Name, transactionId, timeStamp, status, CancellationToken.None);
                TransactionDiagnosticEvents.EmitCancelSendCompleted(
                    resource,
                    transactionId,
                    timeStamp,
                    target,
                    isSelf,
                    status,
                    reason,
                    diagnosticIdentity);
            }
            catch (Exception exception)
            {
                TransactionDiagnosticEvents.EmitCancelSendFailed(
                    resource,
                    transactionId,
                    timeStamp,
                    target,
                    isSelf,
                    status,
                    reason,
                    exception,
                    diagnosticIdentity);
                throw;
            }
        }

        private static bool AreCancelFanOutEventsEnabled()
            => TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.CancelFanOutStarted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.CancelFanOutCompleted))
                || TransactionDiagnosticEvents.IsEnabled(nameof(TransactionDiagnosticEvents.CancelFanOutFailed));

        internal static bool ShouldAbandonCancelFanOut(Task fanOut, Task completed)
            => !ReferenceEquals(completed, fanOut) && !fanOut.IsCompleted;

        private (int TargetCount, int SelfTargetCount) CountCancelTargets(List<ParticipantId> participants)
        {
            var targetCount = 0;
            var selfTargetCount = 0;
            foreach (var participant in participants)
            {
                if (participant.Equals(resource))
                {
                    continue;
                }

                targetCount++;
                if (participant.Reference is not null
                    && resource.Reference is not null
                    && participant.Reference.GrainId == resource.Reference.GrainId)
                {
                    selfTargetCount++;
                }
            }

            return (targetCount, selfTargetCount);
        }

        private void CompleteInDoubtEntryLocally(TransactionRecord<TState> entry, TransactionalStatus status, Exception? exception)
        {
            switch (entry.Role)
            {
                case CommitRole.NotYetDetermined:
                    break;
                case CommitRole.RemoteCommit:
                    entry.ConfirmationResponsePromise?.TrySetException(exception ?? new OrleansException($"Confirm failed: Status {status}"));
                    break;
                case CommitRole.LocalCommit:
                case CommitRole.ReadOnly:
                    if (exception is not null)
                    {
                        entry.PromiseForTA.TrySetException(exception);
                    }
                    else
                    {
                        entry.PromiseForTA.TrySetResult(status);
                    }

                    break;
                default:
                    LogErrorImpossibleCase(entry.Role);
                    throw new NotSupportedException($"{entry.Role} is not a supported CommitRole.");
            }
        }

        private async Task CheckProgressOfCommitQueue()
        {
            if (commitQueue.Count > 0)
            {
                var bottom = commitQueue[0];
                var now = DateTime.UtcNow;

                LogTraceCommitQueueSize(commitQueue.Count, bottom);
                switch (bottom.Role)
                {
                    case CommitRole.LocalCommit:
                        {
                            // 定期检查是否超时。
                            if (bottom.WaitingSince + this.options.PrepareTimeout <= now)
                            {
                                TransactionDiagnosticEvents.EmitPrepareTimedOut(
                                    resource,
                                    bottom.TransactionId,
                                    bottom.Timestamp,
                                    bottom.WaitCount,
                                    bottom.WaitingSince + this.options.PrepareTimeout,
                                    diagnosticIdentity);
                                await AbortCommits(TransactionalStatus.PrepareTimeout);
                                this.RWLock.Notify();
                            }
                            else
                            {
                                storageWorker.Notify(bottom.WaitingSince + this.options.PrepareTimeout);
                            }
                            break;
                        }

                    case CommitRole.RemoteCommit:
                        {
                            if (bottom.PrepareIsPersisted && !bottom.LastSent.HasValue)
                            {
                                // 向远程 TM 发送 PreparedMessage。
                                bottom.TransactionManager.Reference.AsReference<ITransactionManagerExtension>()
                                      .Prepared(
                                          bottom.TransactionManager.Name,
                                          bottom.TransactionId,
                                          bottom.Timestamp,
                                          resource,
                                          TransactionalStatus.Ok,
                                          CancellationToken.None)
                                      .Ignore();

                                bottom.LastSent = now;
                                TransactionDiagnosticEvents.EmitRemotePreparedSent(
                                    resource,
                                    bottom.TransactionId,
                                    bottom.Timestamp,
                                    bottom.TransactionManager,
                                    now,
                                    diagnosticIdentity);

                                LogTraceSentPrepared(bottom);

                                if (bottom.IsReadOnly)
                                {
                                    storageWorker.Notify(); // 已准备好加入批次。
                                }
                                else
                                {
                                    var scheduledAt = bottom.LastSent.Value + this.options.RemoteTransactionPingFrequency;
                                    TransactionDiagnosticEvents.EmitRemoteRecoveryPingScheduled(
                                        resource,
                                        bottom.TransactionId,
                                        bottom.Timestamp,
                                        bottom.TransactionManager,
                                        scheduledAt,
                                        diagnosticIdentity);
                                    storageWorker.Notify(scheduledAt);
                                }
                            }
                            else if (!bottom.IsReadOnly && bottom.LastSent.HasValue)
                            {
                                // 定期发送 ping 消息以重新激活发生故障的 TM。

                                if (bottom.GetNextRemotePingAt(this.options.RemoteTransactionPingFrequency) <= now)
                                {
                                    LogTraceSentPing(bottom);
                                    bottom.TransactionManager.Reference.AsReference<ITransactionManagerExtension>()
                                          .Ping(
                                              bottom.TransactionManager.Name,
                                              bottom.TransactionId,
                                              bottom.Timestamp,
                                              resource,
                                              CancellationToken.None).Ignore();
                                    bottom.RecordRemotePingSent(now);
                                    TransactionDiagnosticEvents.EmitRemoteRecoveryPingSent(
                                        resource,
                                        bottom.TransactionId,
                                        bottom.Timestamp,
                                        bottom.TransactionManager,
                                        now,
                                        diagnosticIdentity);

                                    var scheduledAt = bottom.GetNextRemotePingAt(this.options.RemoteTransactionPingFrequency);
                                    TransactionDiagnosticEvents.EmitRemoteRecoveryPingScheduled(
                                        resource,
                                        bottom.TransactionId,
                                        bottom.Timestamp,
                                        bottom.TransactionManager,
                                        scheduledAt,
                                        diagnosticIdentity);
                                }
                                storageWorker.Notify(bottom.GetNextRemotePingAt(this.options.RemoteTransactionPingFrequency));
                            }

                            break;
                        }

                    default:
                        {
                            LogErrorImpossibleCase(bottom.Role);
                            throw new NotSupportedException($"{bottom.Role} is not a supported CommitRole.");
                        }
                }
            }
        }

        private void CollectEventsForBatch(int batchsize)
        {
            // 收集事件以形成批次。
            for (int i = 0; i < batchsize; i++)
            {
                TransactionRecord<TState> entry = commitQueue[i];
                LogTraceCommitting(entry);

                switch (entry.Role)
                {
                    case CommitRole.LocalCommit:
                        {
                            OnLocalCommit(entry);
                            break;
                        }

                    case CommitRole.RemoteCommit:
                        {
                            if (entry.ConfirmationResponsePromise == null)
                            {
                                // 这是已发送
                                // 已准备消息的只读参与者。
                                // 因此已完成，不需要写入存储或执行其他操作。
                            }
                            else
                            {
                                // 必须在存储中确认，然后响应 TM，使其能够进行清理。
                                this.storageBatch.Confirm(entry.SequenceNumber);
                                this.storageBatch.FollowUpAction(success =>
                                {
                                    if (!success)
                                    {
                                        return;
                                    }

                                    entry.ConfirmationResponsePromise.TrySetResult(true);
                                    LogTraceConfirmedRemoteCommit(entry.SequenceNumber, entry.TransactionId, new(entry.Timestamp), entry.TransactionManager);
                                });
                            }

                            break;
                        }

                    case CommitRole.ReadOnly:
                        {
                            // 当前参与者属于只读事务。必须存储时间戳，然后作出响应。
                            this.storageBatch.Read(entry.Timestamp);
                            this.storageBatch.FollowUpAction(success =>
                            {
                                if (!success)
                                {
                                    return;
                                }

                                entry.PromiseForTA.TrySetResult(TransactionalStatus.Ok);
                            });

                            break;
                        }

                    default:
                        {
                            LogErrorImpossibleCase(entry.Role);
                            throw new NotSupportedException($"{entry.Role} is not a supported CommitRole.");
                        }
                }
            }
        }

        protected virtual void OnLocalCommit(TransactionRecord<TState> entry)
        {
            this.storageBatch.Prepare(entry.SequenceNumber, entry.TransactionId, entry.Timestamp, entry.TransactionManager, entry.State);
            this.storageBatch.Commit(entry.TransactionId, entry.Timestamp, entry.WriteParticipants);
            this.storageBatch.Confirm(entry.SequenceNumber);

            // 写入存储后，向 TA 返回响应。
            this.storageBatch.FollowUpAction(success =>
            {
                if (!success)
                {
                    return;
                }

                LogTraceLocallyCommitted(entry.TransactionId, new(entry.Timestamp));
                entry.PromiseForTA.TrySetResult(TransactionalStatus.Ok);
            });

            if (entry.WriteParticipants.Count > 1)
            {
                // 提交后，需要执行确认及清理任务。
                this.storageBatch.FollowUpAction(success =>
                {
                    if (!success)
                    {
                        return;
                    }

                    LogTraceAddingConfirmationToWorker(entry.TransactionId, new(entry.Timestamp));
                    this.confirmationWorker.Add(entry.TransactionId, entry.Timestamp, entry.WriteParticipants);
                });
            }
            else
            {
                // 没有需要通知的远程写入参与者，因此可以一次完成全部操作。
                this.storageBatch.Collect(entry.TransactionId);
            }
        }

        private async Task AbortCommits(TransactionalStatus status, int from = 0)
        {
            List<Task> pending = new List<Task>();
            // 从指定位置开始清空提交队列的后部。
            for (int i = from; i < commitQueue.Count; i++)
            {
                pending.Add(NotifyOfAbort(commitQueue[i], i == from ? status : TransactionalStatus.CascadingAbort, exception: null));
            }
            commitQueue.RemoveFromBack(commitQueue.Count - from);

            pending.Add(this.RWLock.AbortExecutingTransactions(
                exception: null,
                reason: TransactionDiagnosticEvents.LockBreakReason.TransactionAbort));
            await Task.WhenAll(pending);
        }

        private readonly struct DateTimeLogRecord(DateTime ts)
        {
            public override string ToString() => ts.ToString("O");
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Start two-phase-commit {TransactionId} {Timestamp}"
        )]
        private partial void LogTraceStartTwoPhaseCommit(Guid transactionId, DateTimeLogRecord timeStamp);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Persisted {Record}"
        )]
        private partial void LogTracePersisted(TransactionRecord<TState> record);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Sending immediate prepared {Record}"
        )]
        private partial void LogTraceSendingImmediatePrepared(TransactionRecord<TState> record);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = 777,
            Message = "Internal error: impossible case {CommitRole}"
        )]
        private partial void LogErrorImpossibleCase(CommitRole commitRole);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = $"Transaction abort due to internal error in {nameof(EnqueueCommit)}"
        )]
        private partial void LogErrorTransactionAbortInternalError(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "NotifyOfPrepared - TransactionId:{TransactionId} Timestamp:{Timestamp}, TransactionalStatus{TransactionalStatus}"
        )]
        private partial void LogTraceNotifyOfPrepared(Guid transactionId, DateTimeLogRecord timeStamp, TransactionalStatus transactionalStatus);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = $"Transaction abort due to internal error in {nameof(NotifyOfPrepared)}: Wrong commit type"
        )]
        private partial void LogErrorTransactionAbortWrongCommitType();

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Aborting status={Status} {Entry}"
        )]
        private partial void LogTraceAborting(TransactionalStatus status, TransactionRecord<TState> entry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Aborting via Prepared. Status={Status} Entry={Entry}"
        )]
        private partial void LogTraceAbortingViaPrepared(TransactionalStatus status, TransactionRecord<TState> entry);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to notify all transaction participants of cancellation. TransactionId: {TransactionId}, Timestamp: {Timestamp}, Status: {Status}"
        )]
        private partial void LogWarningFailedToNotifyAllTransactionParticipantsOfCancellation(Guid transactionId, DateTimeLogRecord timeStamp, TransactionalStatus status, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Stopped awaiting transaction cancellation fan-out because the activation is deactivating. TransactionId: {TransactionId}, Timestamp: {Timestamp}, Status: {Status}"
        )]
        private partial void LogWarningCancelFanOutAbandonedDuringDeactivation(Guid transactionId, DateTimeLogRecord timeStamp, TransactionalStatus status, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Stopped awaiting transaction cancellation fan-out after the cleanup budget elapsed. TransactionId: {TransactionId}, Timestamp: {Timestamp}, Status: {Status}, CleanupTimeout: {CleanupTimeout}"
        )]
        private partial void LogWarningCancelFanOutCleanupTimedOut(Guid transactionId, DateTimeLogRecord timeStamp, TransactionalStatus status, TimeSpan cleanupTimeout, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Received ping for {TransactionId}, irrelevant (still processing)"
        )]
        private partial void LogTraceReceivedPingIrrelevant(Guid transactionId);

        //
        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Received ping for {TransactionId}, unknown - presumed abort"
        )]
        private partial void LogTraceReceivedPingUnknown(Guid transactionId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "NotifyOfConfirm: {TransactionId} {TimeStamp}"
        )]
        private partial void LogTraceNotifyOfConfirm(Guid transactionId, DateTimeLogRecord timeStamp);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = $"Internal error in {nameof(NotifyOfConfirm)}: wrong commit type"
        )]
        private partial void LogErrorInternalErrorNotifyOfConfirmWrongCommitType();

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{MethodName}. TransactionId: {TransactionId}, TimeStamp: {TimeStamp} Status: {TransactionalStatus}"
        )]
        private partial void LogTraceNotifyOfCancel(string methodName, Guid transactionId, DateTimeLogRecord timeStamp, TransactionalStatus transactionalStatus);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception in TransactionQueue"
        )]
        private partial void LogWarningExceptionInTransactionQueue(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Load v{StableSequenceNumber} {PendingStatesCount}p {CommitRecordsCount}c"
        )]
        private partial void LogDebugLoad(long stableSequenceNumber, int pendingStatesCount, int commitRecordsCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Recover two-phase-commit {TransactionId}"
        )]
        private partial void LogDebugRecoverTwoPhaseCommit(string transactionId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Recover commit confirmation {Key}"
        )]
        private partial void LogDebugRecoverCommitConfirmation(Guid key);

        private readonly struct CommitQueueLogRecord(CommitQueue<TState> state, int committableEntries)
        {
            public override string ToString() => state.Count > committableEntries ? state[committableEntries].ToString() : "";
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BatchCommit: {CommittableEntries} Leave: {UncommittableEntries}, Record: {Record}"
        )]
        private partial void LogDebugBatchCommit(int committableEntries, int uncommittableEntries, CommitQueueLogRecord record);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Store pre conditions not met."
        )]
        private partial void LogWarningStorePreConditionsNotMet();

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = 888,
            Message = "Reload from storage triggered by e-tag mismatch."
        )]
        private partial void LogWarningReloadFromStorageTriggeredByETagMismatch(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Storage exception in storage worker."
        )]
        private partial void LogWarningStorageExceptionInStorageWorker(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Stable state version: {StableSequenceNumber}"
        )]
        private partial void LogTraceStableStateVersion(long stableSequenceNumber);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = 888,
            Message = "Exception in storageWorker. Retry {FailCounter}"
        )]
        private partial void LogWarningExceptionInStorageWorker(int failCounter, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "StorageWorker triggering grain Deactivation"
        )]
        private partial void LogDebugStorageWorkerTriggeringGrainDeactivation();

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{CommitQueueSize} entries in queue waiting for bottom: {BottomEntry}"
        )]
        private partial void LogTraceCommitQueueSize(int commitQueueSize, TransactionRecord<TState> bottomEntry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Sent Prepared {BottomEntry}"
        )]
        private partial void LogTraceSentPrepared(TransactionRecord<TState> bottomEntry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Sent ping {BottomEntry}"
        )]
        private partial void LogTraceSentPing(TransactionRecord<TState> bottomEntry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Committing {Entry}"
        )]
        private partial void LogTraceCommitting(TransactionRecord<TState> entry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Confirmed remote commit v{SequenceNumber}. TransactionId:{TransactionId} Timestamp:{Timestamp} TransactionManager:{TransactionManager}"
        )]
        private partial void LogTraceConfirmedRemoteCommit(long sequenceNumber, Guid transactionId, DateTimeLogRecord timeStamp, ParticipantId transactionManager);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Locally committed {TransactionId} {Timestamp}"
        )]
        private partial void LogTraceLocallyCommitted(Guid transactionId, DateTimeLogRecord timeStamp);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Adding confirmation to worker for {TransactionId} {Timestamp}"
        )]
        private partial void LogTraceAddingConfirmationToWorker(Guid transactionId, DateTimeLogRecord timeStamp);
    }
}
