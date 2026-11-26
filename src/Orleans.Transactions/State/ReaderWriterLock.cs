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

        // 链接的锁定组列表
        // 头部是目前持有锁定的组
        private LockGroup? currentGroup;

        // 缓存最后已知最小值的缓存, 所以我们不必重新计算
        private DateTime cachedMin = DateTime.MaxValue;
        private Guid cachedMinId;

        // 集体获取/解除锁
        private class LockGroup : Dictionary<Guid, TransactionRecord<TState>>
        {
            public int FillCount;
            public List<PendingOperation>? PendingOperations;
            public LockGroup? Next; // 等待获取锁定的排队待取交易
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

            // 搜索当前交易
            if (Find(transactionId, isRead && !exclusiveLock, out var group, out var record))
            {
                // 检查是否我们已经丢失了一些读或写
                if (counter.Reads > record.NumberReads || counter.Writes > record.NumberWrites)
                {
                    throw new OrleansBrokenTransactionLockException(transactionId.ToString(), "when re-entering lock");
                }

                // 检查操作是否与该组中的其他交易冲突
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
                        // 将所有冲突推回
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
                // 检查是否我们本该已经锁定
                if (counter.Reads + counter.Writes > 0)
                {
                    throw new OrleansBrokenTransactionLockException(transactionId.ToString(), "when trying to re-enter lock");
                }

                var now = DateTime.UtcNow;
                var lockTimeout = GetEffectiveLockTimeout(transactionTimeout, this.options.LockTimeout);

                // 更新锁定截止时间
                if (group == currentGroup)
                {
                    var deadline = AddTimeout(now, lockTimeout);
                    if (!group.Deadline.HasValue || group.Deadline.Value < deadline)
                    {
                        group.Deadline = deadline;
                    }
                    LogTraceSetLockExpiration(new(group.Deadline));
                }

                // 为此交易创建新记录
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
                // 组获得锁定后,任务将执行

                if (group.PendingOperations == null)
                    group.PendingOperations = new List<PendingOperation>();

                group.PendingOperations.Add(new PendingOperation(transactionId, completion, abort));
            }
            else
            {
                // 执行任务
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
            // 如果交易从未发生或已经退缩,则不op
            if (!TryRemove(guid, out var record))
            {
                return Task.CompletedTask;
            }

            // 通知远程听众
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
                    // 检查是否有任何组合成员准备退出锁定
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
                                // 锁定组超时了 。
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

                                // 当组合过期时再次检查
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
                        // 锁定为空,新组合可以输入
                        currentGroup = currentGroup.Next;

                        if (currentGroup != null)
                        {
                            // 无法成功时的过期服务过者
                            // 因为他们等待锁的时间比
                            // 交易总交易总超时
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

                            // 执行全部读和更新任务
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

                    // 如果我们还没有找到地方插入这一行动,而且有空间,没有冲突,就使用这个
                    if (group == null
                        && pos.FillCount < this.options.MaxLockGroupSize
                        && !HasConflict(isRead, DateTime.MaxValue, guid, pos, out _))
                    {
                        group = pos;
                    }

                    if (pos.Next == null) // 我们没有发现这个tx。
                    {
                        // 添加一个新的空组以插入此 tx 。 如果我们尚未找到该 tx , 则要插入
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

            // 单元素的快速路径
            if (currentGroup!.Count == 1)
            {
                var kvp = currentGroup.First();
                if (kvp.Value.Role == CommitRole.NotYetDetermined) // 未收到TA公司承诺
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
                // 找到当前最小值, 如果我们没有有效的缓存
                if (cachedMin == DateTime.MaxValue
                    || !currentGroup.TryGetValue(cachedMinId, out var record)
                    || record.Role != CommitRole.NotYetDetermined
                    || record.Timestamp != cachedMin)
                {
                    cachedMin = DateTime.MaxValue;
                    foreach (var kvp in currentGroup)
                    {
                        if (kvp.Value.Role == CommitRole.NotYetDetermined) // 未收到TA公司承诺
                        {
                            if (cachedMin > kvp.Value.Timestamp)
                            {
                                cachedMin = kvp.Value.Timestamp;
                                cachedMinId = kvp.Key;
                            }
                        }
                    }
                }

                // 查找已释放条目
                foreach (var kvp in currentGroup)
                {
                    if (kvp.Value.Role != CommitRole.NotYetDetermined) // 准备承诺
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

        // "内锁{钥匙}"
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
