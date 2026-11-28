using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.Diagnostics;

namespace Orleans.Transactions.State
{
    internal partial class ReadWriteLock<TState>
       where TState : class, new()
    {
        private readonly TransactionalStateOptions options;
        private readonly TransactionQueue<TState> queue;
        private readonly BatchWorker lockWorker;
        private readonly BatchWorker storageWorker;
        private readonly ILogger logger;
        private readonly CancellationToken onDeactivating;

        // 锁组的链表。
        // 链表头是当前持有锁的组。
        private LockGroup? currentGroup;

        // 缓存最后已知的最小值，减少重复计算。
        private DateTime cachedMin = DateTime.MaxValue;
        private Guid cachedMinId;

        // 由彼此不冲突的事务组成的组，共同获取或释放锁。
        private class LockGroup : Dictionary<Guid, TransactionRecord<TState>>
        {
            public int FillCount;
            public List<PendingOperation>? PendingOperations;
            public LockGroup? Next; // 正在排队等待获取锁的事务。
            public DateTime? Deadline;
            public void Reset()
            {
                FillCount = 0;
                PendingOperations = null;
                Deadline = null;
                Clear();
            }
        }

        private readonly record struct PendingOperation(Guid TransactionId, Action Execute, Action Abort);

        public ReadWriteLock(
            IOptions<TransactionalStateOptions> options,
            TransactionQueue<TState> queue,
            BatchWorker storageWorker,
            ILogger logger,
            CancellationToken onDeactivating)
        {
            this.options = options.Value;
            this.queue = queue;
            this.storageWorker = storageWorker;
            this.logger = logger;
            this.onDeactivating = onDeactivating;
            this.lockWorker = new BatchWorkerFromDelegate(LockWork, onDeactivating);
        }

        public async Task<TResult> EnterLock<TResult>(
            Guid transactionId,
            DateTime priority,
            TimeSpan transactionTimeout,
            AccessCounter counter,
            bool isRead,
            bool exclusiveLock,
            Func<TResult> task)
        {
            bool rollbacksOccurred = false;
            List<Task> cleanup = new List<Task>();

            await this.queue.Ready(transactionId);

            // 搜索活跃事务。
            if (Find(transactionId, isRead && !exclusiveLock, out var group, out var record))
            {
                // 检查是否已丢失某些读操作或写操作。
                if (counter.Reads > record.NumberReads || counter.Writes > record.NumberWrites)
                {
                    throw new OrleansBrokenTransactionLockException(transactionId.ToString(), "when re-entering lock");
                }

                // 检查该操作是否与组内其他事务冲突。
                if (HasConflict(isRead && !exclusiveLock, priority, transactionId, group, out var resolvable))
                {
                    if (!resolvable)
                    {
                        Rollback(transactionId);
                        lockWorker.Notify();
                        throw new OrleansTransactionLockUpgradeException(transactionId.ToString());
                    }
                    else
                    {
                        // 回滚所有冲突的事务。
                        var conflicts = Conflicts(transactionId, group).ToList();

                        if (conflicts.Count > 0)
                        {
                            foreach (var r in conflicts)
                            {
                                cleanup.Add(Rollback(r, true, TransactionDiagnosticEvents.LockBreakReason.Conflict));
                                rollbacksOccurred = true;
                            }
                        }
                    }
                }
            }
            else
            {
                // 检查当前事务是否本应已持有此锁。
                if (counter.Reads + counter.Writes > 0)
                {
                    throw new OrleansBrokenTransactionLockException(transactionId.ToString(), "when trying to re-enter lock");
                }

                var now = DateTime.UtcNow;
                var lockTimeout = GetEffectiveLockTimeout(transactionTimeout, this.options.LockTimeout);

                // 更新锁的截止时间。
                if (group == currentGroup)
                {
                    var deadline = AddTimeout(now, lockTimeout);
                    if (!group.Deadline.HasValue || group.Deadline.Value < deadline)
                    {
                        group.Deadline = deadline;
                    }
                    LogTraceSetLockExpiration(new(group.Deadline));
                }

                // 为此事务创建新记录。
                record = new TransactionRecord<TState>()
                {
                    TransactionId = transactionId,
                    Priority = priority,
                    Deadline = AddTimeout(now, this.options.LockAcquireTimeout),
                    LockTimeout = lockTimeout
                };

                group.Add(transactionId, record);
                group.FillCount++;

                if (group == currentGroup)
                    LogTraceEnterLock(transactionId, group.FillCount);
                else
                    LogTraceEnterLockQueue(transactionId, group.FillCount);
            }

            var result =
                new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            void completion()
            {
                try
                {
                    result.TrySetResult(task());
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                }
            }
            void abort() => result.TrySetException(new OrleansCascadingAbortException(transactionId.ToString()));

            if (group != currentGroup)
            {
                // 所属组获得锁后将执行该任务。

                if (group.PendingOperations == null)
                    group.PendingOperations = new List<PendingOperation>();

                group.PendingOperations.Add(new PendingOperation(transactionId, completion, abort));
            }
            else
            {
                // 立即执行任务。
                completion();
            }

            if (!isRead || exclusiveLock)
            {
                record.IsExclusiveLock = true;
            }

            if (isRead)
            {
                record.AddRead();
            }
            else
            {
                record.AddWrite();
            }

            if (rollbacksOccurred)
            {
                lockWorker.Notify();
            }
            else if (group.Deadline.HasValue)
            {
                lockWorker.Notify(group.Deadline.Value);
            }

            await Task.WhenAll(cleanup);
            return await result.Task;
        }

        public async Task<(TransactionalStatus Status, TransactionRecord<TState> State)> ValidateLock(Guid transactionId, AccessCounter accessCount)
        {
            if (currentGroup == null || !currentGroup.TryGetValue(transactionId, out TransactionRecord<TState>? record))
            {
                return (TransactionalStatus.BrokenLock, new TransactionRecord<TState> { TransactionId = transactionId });
            }
            else if (record.NumberReads != accessCount.Reads
                   || record.NumberWrites != accessCount.Writes)
            {
                await Rollback(transactionId, true, TransactionDiagnosticEvents.LockBreakReason.ValidationFailure);
                return (TransactionalStatus.LockValidationFailed, record);
            }
            else
            {
                return (TransactionalStatus.Ok, record);
            }
        }

        public void Notify()
        {
            this.lockWorker.Notify();
        }

        public bool TryGetRecord(Guid transactionId, [NotNullWhen(true)] out TransactionRecord<TState>? record)
        {
            return this.currentGroup!.TryGetValue(transactionId, out record);
        }

        public Task AbortExecutingTransactions(
            Exception? exception,
            TransactionDiagnosticEvents.LockBreakReason reason = TransactionDiagnosticEvents.LockBreakReason.TransactionAbort)
        {
            if (currentGroup != null)
            {
                Task[] pending = currentGroup.Select(g => BreakLock(g.Key, g.Value, exception, reason)).ToArray();
                currentGroup.Reset();
                return Task.WhenAll(pending);
            }
            return Task.CompletedTask;
        }

        private Task BreakLock(
            Guid transactionId,
            TransactionRecord<TState> entry,
            Exception? exception,
            TransactionDiagnosticEvents.LockBreakReason reason)
        {
            LogTraceBreakLock(transactionId);
            TransactionDiagnosticEvents.EmitLockBroken(
                queue.Resource,
                transactionId,
                reason,
                queue.DiagnosticIdentity);
            return this.queue.NotifyOfAbort(entry, TransactionalStatus.BrokenLock, exception);
        }

        public void AbortQueuedTransactions()
        {
            var pos = currentGroup?.Next;
            while (pos != null)
            {
                if (pos.PendingOperations != null)
                {
                    foreach (var operation in pos.PendingOperations)
                    {
                        operation.Abort();
                    }
                    pos.PendingOperations = null;
                }
                pos.Clear();
                pos = pos.Next;
            }
            if (currentGroup != null)
                currentGroup.Next = null;
        }

        public void Rollback(Guid guid)
        {
            TryRemove(guid, out _);
        }

        public Task Rollback(Guid guid, bool notify, TransactionDiagnosticEvents.LockBreakReason reason)
        {
            // 如果事务从未发生或已经回滚，则不执行任何操作。
            if (!TryRemove(guid, out var record))
            {
                return Task.CompletedTask;
            }

            // 通知远程监听方。
            if (!notify)
            {
                return Task.CompletedTask;
            }

            TransactionDiagnosticEvents.EmitLockBroken(
                queue.Resource,
                guid,
                reason,
                queue.DiagnosticIdentity);
            return queue.NotifyOfAbort(record, TransactionalStatus.BrokenLock, exception: null);
        }

        private bool TryRemove(Guid transactionId, [NotNullWhen(true)] out TransactionRecord<TState>? record)
        {
            var group = currentGroup;
            while (group != null)
            {
                if (group.Remove(transactionId, out record))
                {
                    if (group != currentGroup)
                    {
                        AbortPendingOperations(group, transactionId);
                    }

                    return true;
                }

                group = group.Next;
            }

            record = null;
            return false;
        }

        private async Task LockWork()
        {
            if (!this.onDeactivating.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if (currentGroup != null)
                {
                    // 检查组内是否有成员已准备释放锁。
                    if (currentGroup.Count > 0)
                    {
                        if (LockExits(out var single, out var multiple))
                        {
                            if (single != null)
                            {
                                await this.queue.EnqueueCommit(single);
                            }
                            else if (multiple != null)
                            {
                                foreach (var r in multiple)
                                {
                                    await this.queue.EnqueueCommit(r);
                                }
                            }

                            lockWorker.Notify();
                            storageWorker.Notify();
                        }

                        else if (currentGroup.Deadline.HasValue)
                        {
                            if (currentGroup.Deadline.Value < now)
                            {
                                // 锁组已超时。
                                TimeSpan late = now - currentGroup.Deadline.Value;
                                LogTraceBreakLockTimeout(new(currentGroup.Keys), Math.Floor(late.TotalMilliseconds));
                                foreach (var transactionId in currentGroup.Keys)
                                {
                                    TransactionDiagnosticEvents.EmitLockExpired(
                                        queue.Resource,
                                        transactionId,
                                        currentGroup.Deadline.Value,
                                        now,
                                        TransactionDiagnosticEvents.LockExpirationKind.HeldLock,
                                        queue.DiagnosticIdentity);
                                }
                                await AbortExecutingTransactions(
                                    exception: null,
                                    reason: TransactionDiagnosticEvents.LockBreakReason.Expired);
                                lockWorker.Notify();
                            }
                            else
                            {
                                LogTraceRecheckLockExpiration(new(currentGroup.Deadline));

                                // 在组到期时再次检查。
                                lockWorker.Notify(currentGroup.Deadline.Value);
                            }
                        }
                        else
                        {
                            LogWarningDeadlineNotSet(new(currentGroup.Keys));
                        }
                    }

                    else
                    {
                        // 当前锁无人持有，新组可以进入。
                        currentGroup = currentGroup.Next;

                        if (currentGroup != null)
                        {
                            // 丢弃没有机会成功的、已过期的等待者，
                            // 因为其等待锁的时间已经超过
                            // 事务的总超时时间。
                            var expiredWaiters = currentGroup
                                .Where(kvp => now > kvp.Value.Deadline)
                                .Select(kvp => kvp.Key)
                                .ToList();
                            foreach (var transactionId in expiredWaiters)
                            {
                                var deadline = currentGroup[transactionId].Deadline;
                                TransactionDiagnosticEvents.EmitLockExpired(
                                    queue.Resource,
                                    transactionId,
                                    deadline,
                                    now,
                                    TransactionDiagnosticEvents.LockExpirationKind.QueuedWaiter,
                                    queue.DiagnosticIdentity);
                                currentGroup.Remove(transactionId);
                                AbortPendingOperations(currentGroup, transactionId);
                                LogTraceExpireLockWaiter(transactionId);
                            }

                            currentGroup.Deadline = currentGroup.Count == 0
                                ? null
                                : AddTimeout(now, currentGroup.Values.Max(record => record.LockTimeout));

                            LogTraceLockGroupSize(currentGroup.Count, new(currentGroup.Deadline));
                            if (logger.IsEnabled(LogLevel.Trace))
                            {
                                foreach (var kvp in currentGroup)
                                    LogTraceEnterLockKey(kvp.Key);
                            }

                            // 执行所有读取和更新任务。
                            if (currentGroup.PendingOperations != null)
                            {
                                var pendingOperations = currentGroup.PendingOperations;
                                currentGroup.PendingOperations = null;
                                foreach (var operation in pendingOperations)
                                {
                                    operation.Execute();
                                }
                            }

                            lockWorker.Notify();
                        }
                    }
                }
            }
        }

        internal DateTime? CurrentGroupDeadline => currentGroup?.Deadline;

        internal static TimeSpan GetEffectiveLockTimeout(TimeSpan transactionTimeout, TimeSpan configuredLockTimeout)
            => transactionTimeout > configuredLockTimeout ? transactionTimeout : configuredLockTimeout;

        private static DateTime AddTimeout(DateTime now, TimeSpan timeout)
            => timeout >= DateTime.MaxValue - now ? DateTime.MaxValue : now + timeout;

        private static void AbortPendingOperations(LockGroup group, Guid transactionId)
        {
            if (group.PendingOperations == null)
            {
                return;
            }

            for (var i = group.PendingOperations.Count - 1; i >= 0; i--)
            {
                var operation = group.PendingOperations[i];
                if (operation.TransactionId == transactionId)
                {
                    group.PendingOperations.RemoveAt(i);
                    operation.Abort();
                }
            }

            if (group.PendingOperations.Count == 0)
            {
                group.PendingOperations = null;
            }
        }

        private bool Find(Guid guid, bool isRead, out LockGroup group, [NotNullWhen(true)] out TransactionRecord<TState>? record)
        {
            if (currentGroup == null)
            {
                group = currentGroup = new LockGroup();
                record = null;
                return false;
            }
            else
            {
                group = null!;
                var pos = currentGroup;

                while (true)
                {
                    if (pos.TryGetValue(guid, out record))
                    {
                        group = pos;
                        return true;
                    }

                    // 如果尚未找到插入此操作的位置，且当前组有空间并且没有冲突，则使用当前组。
                    if (group == null
                        && pos.FillCount < this.options.MaxLockGroupSize
                        && !HasConflict(isRead, DateTime.MaxValue, guid, pos, out _))
                    {
                        group = pos;
                    }

                    if (pos.Next == null) // 未找到此事务。
                    {
                        // 如果尚未找到适当的组，则添加一个空组以插入此事务。
                        if (group == null)
                        {
                            group = pos.Next = new LockGroup();
                        }

                        return false;
                    }

                    pos = pos.Next;
                }
            }
        }

        private static bool HasConflict(bool isRead, DateTime priority, Guid transactionId, LockGroup group, out bool resolvable)
        {
            bool foundResolvableConflicts = false;

            foreach (var kvp in group)
            {
                if (kvp.Key != transactionId)
                {
                    if (isRead && kvp.Value.NumberWrites == 0 && !kvp.Value.IsExclusiveLock)
                    {
                        continue;
                    }
                    else
                    {
                        if (priority > kvp.Value.Priority)
                        {
                            resolvable = false;
                            return true;
                        }
                        else
                        {
                            foundResolvableConflicts = true;
                        }
                    }
                }
            }

            resolvable = foundResolvableConflicts;
            return foundResolvableConflicts;
        }

        private static IEnumerable<Guid> Conflicts(Guid transactionId, LockGroup group)
        {
            foreach (var kvp in group)
            {
                if (kvp.Key != transactionId)
                {
                    yield return kvp.Key;
                }
            }
        }

        private bool LockExits(out TransactionRecord<TState>? single, out List<TransactionRecord<TState>>? multiple)
        {
            single = null;
            multiple = null;

            // 对仅有一个元素的情形使用快速路径。
            if (currentGroup!.Count == 1)
            {
                var kvp = currentGroup.First();
                if (kvp.Value.Role == CommitRole.NotYetDetermined) // 尚未收到来自 TA 的提交消息。
                {
                    return false;
                }
                else
                {
                    single = kvp.Value;

                    currentGroup.Remove(single.TransactionId);
                    LogDebugExitLock(single.TransactionId, new(single.Timestamp));
                    return true;
                }
            }
            else
            {
                // 如果没有有效的最小值缓存，则查找当前最小值。
                if (cachedMin == DateTime.MaxValue
                    || !currentGroup.TryGetValue(cachedMinId, out var record)
                    || record.Role != CommitRole.NotYetDetermined
                    || record.Timestamp != cachedMin)
                {
                    cachedMin = DateTime.MaxValue;
                    foreach (var kvp in currentGroup)
                    {
                        if (kvp.Value.Role == CommitRole.NotYetDetermined) // 尚未收到来自 TA 的提交消息。
                        {
                            if (cachedMin > kvp.Value.Timestamp)
                            {
                                cachedMin = kvp.Value.Timestamp;
                                cachedMinId = kvp.Key;
                            }
                        }
                    }
                }

                // 查找已释放的条目。
                foreach (var kvp in currentGroup)
                {
                    if (kvp.Value.Role != CommitRole.NotYetDetermined) // 已准备好提交。
                    {
                        if (kvp.Value.Timestamp < cachedMin)
                        {
                            if (multiple == null)
                            {
                                multiple = new List<TransactionRecord<TState>>();
                            }
                            multiple.Add(kvp.Value);
                        }
                    }
                }

                if (multiple == null)
                {
                    return false;
                }
                else
                {
                    multiple.Sort(Comparer);

                    for (int i = 0; i < multiple.Count; i++)
                    {
                        currentGroup.Remove(multiple[i].TransactionId);
                        LogDebugExitLockProgress(i, multiple.Count, multiple[i].TransactionId, new(multiple[i].Timestamp));
                    }

                    return true;
                }
            }
        }

        private static int Comparer(TransactionRecord<TState> a, TransactionRecord<TState> b)
        {
            return a.Timestamp.CompareTo(b.Timestamp);
        }

        private readonly struct DateTimeLogRecord(DateTime? ts)
        {
            public override string ToString() => ts?.ToString("o") ?? "none";
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Set lock expiration at {Deadline}"
        )]
        private partial void LogTraceSetLockExpiration(DateTimeLogRecord deadline);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Enter-lock {TransactionId} Fill count={FillCount}"
        )]
        private partial void LogTraceEnterLock(Guid transactionId, int fillCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Enter-lock-queue {TransactionId} Fill count={FillCount}"
        )]
        private partial void LogTraceEnterLockQueue(Guid transactionId, int fillCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Break-lock for transaction {TransactionId}"
        )]
        private partial void LogTraceBreakLock(Guid transactionId);

        private readonly struct TransactionIdsLogRecord(IEnumerable<Guid> guids)
        {
            public override string ToString() => string.Join(",", guids);
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Break-lock timeout for transactions {TransactionIds}. {Late}ms late"
        )]
        private partial void LogTraceBreakLockTimeout(TransactionIdsLogRecord transactionIds, double late);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Recheck lock expiration at {Deadline}"
        )]
        private partial void LogTraceRecheckLockExpiration(DateTimeLogRecord deadline);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Deadline not set for transactions {TransactionIds}"
        )]
        private partial void LogWarningDeadlineNotSet(TransactionIdsLogRecord transactionIds);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Expire-lock-waiter {Key}"
        )]
        private partial void LogTraceExpireLockWaiter(Guid key);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Lock group size={Count} deadline={Deadline}"
        )]
        private partial void LogTraceLockGroupSize(int count, DateTimeLogRecord deadline);

        // "Enter-lock {Key}"
        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Enter-lock {Key}"
        )]
        private partial void LogTraceEnterLockKey(Guid key);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Exit-lock {TransactionId} {Timestamp}"
        )]
        private partial void LogDebugExitLock(Guid transactionId, DateTimeLogRecord timestamp);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Exit-lock ({Current}/{Count}) {TransactionId} {Timestamp}"
        )]
        private partial void LogDebugExitLockProgress(int current, int count, Guid transactionId, DateTimeLogRecord timestamp);
    }
}
