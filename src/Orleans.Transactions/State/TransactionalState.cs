using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.Diagnostics;
using Orleans.Transactions.State;
using Orleans.Configuration;
using Orleans.Timers.Internal;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供参与 Orleans 事务的有状态组件（facet）。
    /// </summary>
    /// <typeparam name="TState">事务状态类型。</typeparam>
    public partial class TransactionalState<TState> : ITransactionalState<TState>, ILifecycleParticipant<IGrainLifecycle>
        where TState : class, new()
    {
        private readonly TransactionalStateConfiguration config;
        private readonly IGrainContext context;
        private readonly ITransactionDataCopier<TState> copier;
        private readonly Dictionary<Type, object> copiers;
        private readonly IGrainRuntime grainRuntime;
        private readonly ILogger logger;
        private ParticipantId participantId;
        private TransactionQueue<TState> queue = null!;

        /// <summary>
        /// 获取当前事务的标识符。
        /// </summary>
        public string CurrentTransactionId => TransactionContext.GetRequiredTransactionInfo().Id;

        private bool detectReentrancy;

        /// <summary>
        /// 初始化 <see cref="TransactionalState{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="transactionalStateConfiguration">事务状态配置。</param>
        /// <param name="contextAccessor">当前 Grain 上下文的访问器。</param>
        /// <param name="copier">用于隔离各事务状态版本的复制器。</param>
        /// <param name="grainRuntime">Grain 运行时。</param>
        /// <param name="logger">日志记录器。</param>
        public TransactionalState(
            TransactionalStateConfiguration transactionalStateConfiguration,
            IGrainContextAccessor contextAccessor,
            ITransactionDataCopier<TState> copier,
            IGrainRuntime grainRuntime,
            ILogger<TransactionalState<TState>> logger)
        {
            this.config = transactionalStateConfiguration;
            this.context = contextAccessor.GrainContext;
            this.copier = copier;
            this.grainRuntime = grainRuntime;
            this.logger = logger;
            this.copiers = new Dictionary<Type, object>();
            this.copiers.Add(typeof(TState), copier);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> 为 null。</exception>
        public Task<TResult> PerformRead<TResult>(Func<TState, TResult> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (detectReentrancy)
            {
                throw new LockRecursionException("Cannot perform a read operation from within another operation");
            }

            var info = TransactionContext.GetRequiredTransactionInfo();
            LogTraceStartRead(info);

            info.Participants.TryGetValue(this.participantId, out var recordedaccesses);

            // 调度读取访问，使其在持有锁时进行。
            return this.queue.RWLock.EnterLock<TResult>(info.TransactionId, info.Priority, info.Timeout, recordedaccesses, true, info.UseExclusiveLock,
                () =>
                {
                    // 检查事务是否因等待期间过期而丢失了对应记录。
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<TState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到事务时间戳中。
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    LogDebugUpdateLockRead(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 在事务信息数据结构中记录此次读取。
                    info.RecordRead(this.participantId, record.Timestamp);

                    // 执行读取。
                    TResult result = default!;
                    try
                    {
                        detectReentrancy = true;

                        result = CopyResult(operation(record.State));
                    }
                    finally
                    {
                        LogTraceEndRead(info, result, record.State);
                        detectReentrancy = false;
                    }

                    return result;
                });
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> 为 null。</exception>
        public Task<TResult> PerformRead<TResult>(Func<TState, TResult> operation, ReadIsolation isolation)
        {
            return isolation switch
            {
                ReadIsolation.Serializable => PerformRead(operation),
                ReadIsolation.ReadCommitted => PerformReadCommitted(operation),
                _ => throw new ArgumentOutOfRangeException(nameof(isolation), isolation, "Unknown read isolation."),
            };
        }

        private async Task<TResult> PerformReadCommitted<TResult>(Func<TState, TResult> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (detectReentrancy)
            {
                throw new LockRecursionException("Cannot perform a read operation from within another operation");
            }

            // 已提交快照处于环境事务所持有的锁之外，因此会破坏可串行化保证。
            if (TransactionContext.GetTransactionInfo() is { } info)
            {
                throw new OrleansTransactionException($"A read-committed read cannot be performed inside transaction {info.Id}.");
            }

            await this.queue.Ready();

            this.queue.GetCommittedState(out var state, out var sequenceNumber);
            LogDebugReadCommitted(sequenceNumber);

            try
            {
                detectReentrancy = true;
                return CopyResult(operation(state));
            }
            finally
            {
                detectReentrancy = false;
            }
        }

        /// <inheritdoc/>
        public Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateAction)
        {
            if (updateAction == null) throw new ArgumentNullException(nameof(updateAction));
            if (detectReentrancy)
            {
                throw new LockRecursionException("Cannot perform an update operation from within another operation");
            }

            var info = TransactionContext.GetRequiredTransactionInfo();

            LogTraceStartWrite(info);

            if (info.IsReadOnly)
            {
                throw new OrleansReadOnlyViolatedException(info.Id);
            }

            info.Participants.TryGetValue(this.participantId, out var recordedaccesses);

            return this.queue.RWLock.EnterLock<TResult>(info.TransactionId, info.Priority, info.Timeout, recordedaccesses, false, info.UseExclusiveLock,
                () =>
                {
                    // 检查事务是否在等待期间过期。
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<TState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到事务时间戳中。
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    // 链接到最新状态。
                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    // 如果这是第一次写入，则创建状态的深拷贝。
                    if (!record.HasCopiedState)
                    {
                        record.State = this.copier.DeepCopy(record.State);
                        record.SequenceNumber++;
                        record.HasCopiedState = true;
                    }

                    LogDebugUpdateLockWrite(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 在事务信息数据结构中记录此次写入。
                    info.RecordWrite(this.participantId, record.Timestamp);

                    // 执行写入。
                    try
                    {
                        detectReentrancy = true;

                        return CopyResult(updateAction(record.State));
                    }
                    finally
                    {
                        LogTraceEndWrite(info, record.TransactionId, new(record.Timestamp));
                        detectReentrancy = false;
                    }
                }
            );
        }

        /// <inheritdoc />
        public void Participate(IGrainLifecycle lifecycle) => Participate(lifecycle, SetupResourceFactory);

        internal void Participate(IGrainLifecycle lifecycle, Action<IGrainContext, string, TransactionQueue<TState>> setupResourceFactory)
        {
            lifecycle.Subscribe<TransactionalState<TState>>(GrainLifecycleStage.SetupState, (ct) => OnSetupState(setupResourceFactory, ct), OnStop);
            lifecycle.Subscribe<TransactionalState<TState>>(GrainLifecycleStage.Last, static _ => Task.CompletedTask, OnStop);
        }

        // 初始化可能在创建队列之前失败。
        private Task OnStop(CancellationToken ct) => this.queue?.StopAsync(ct) ?? Task.CompletedTask;

        private static void SetupResourceFactory(IGrainContext context, string stateName, TransactionQueue<TState> queue)
        {
            // 将资源工厂添加到 Grain 上下文中。
            context.RegisterResourceFactory<ITransactionalResource>(stateName, () => new TransactionalResource<TState>(queue));

            // 将 TM 工厂添加到 Grain 上下文中。
            context.RegisterResourceFactory<ITransactionManager>(stateName, () => new TransactionManager<TState>(queue));
        }

        private async Task OnSetupState(Action<IGrainContext, string, TransactionQueue<TState>> setupResourceFactory, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            this.participantId = new ParticipantId(this.config.StateName, this.context.GrainReference, this.config.SupportedRoles);

            var storageFactory = this.context.ActivationServices.GetRequiredService<INamedTransactionalStateStorageFactory>();
            ITransactionalStateStorage<TState> storage = storageFactory.Create<TState>(this.config.StorageName, this.config.StateName);

            // 初始化事务处理管道。
            void deactivate() => grainRuntime.DeactivateOnIdle(context);
            var options = this.context.ActivationServices.GetRequiredService<IOptions<TransactionalStateOptions>>();
            var clock = this.context.ActivationServices.GetRequiredService<IClock>();
            var timerManager = this.context.ActivationServices.GetRequiredService<ITimerManager>();
            var diagnosticIdentity = new TransactionDiagnosticEvents.TransactionDiagnosticIdentity(
                this.context.Address.SiloAddress,
                this.context.ActivationId);
            this.queue = new TransactionQueue<TState>(
                options,
                this.participantId,
                deactivate,
                storage,
                clock,
                logger,
                timerManager,
                diagnosticIdentity);

            setupResourceFactory(this.context, this.config.StateName, queue);

            // 恢复状态。
            await this.queue.NotifyOfRestore();
        }

        private TResult CopyResult<TResult>(TResult result)
        {
            ITransactionDataCopier<TResult> resultCopier;
            if (!this.copiers.TryGetValue(typeof(TResult), out object? cp))
            {
                resultCopier = this.context.ActivationServices.GetRequiredService<ITransactionDataCopier<TResult>>();
                this.copiers.Add(typeof(TResult), resultCopier);
            }
            else
            {
                resultCopier = (ITransactionDataCopier<TResult>)cp;
            }
            return resultCopier.DeepCopy(result);
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "StartRead {Info}"
        )]
        private partial void LogTraceStartRead(TransactionInfo info);

        private readonly struct DateTimeLogRecord(DateTime ts)
        {
            public override string ToString() => ts.ToString("o");
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Update-lock read v{SequenceNumber} {TransactionId} {Timestamp}"
        )]
        private partial void LogDebugUpdateLockRead(long sequenceNumber, Guid transactionId, DateTimeLogRecord timestamp);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Read-committed read v{SequenceNumber}"
        )]
        private partial void LogDebugReadCommitted(long sequenceNumber);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "EndRead {Info} {Result} {State}"
        )]
        private partial void LogTraceEndRead(TransactionInfo info, object? result, TState state);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "StartWrite {Info}"
        )]
        private partial void LogTraceStartWrite(TransactionInfo info);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Update-lock write v{SequenceNumber} {TransactionId} {Timestamp}"
        )]
        private partial void LogDebugUpdateLockWrite(long sequenceNumber, Guid transactionId, DateTimeLogRecord timestamp);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "EndWrite {Info} {TransactionId} {Timestamp}"
        )]
        private partial void LogTraceEndWrite(TransactionInfo info, Guid transactionId, DateTimeLogRecord timestamp);
    }
}
