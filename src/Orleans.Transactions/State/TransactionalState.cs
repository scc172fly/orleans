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
    /// 提供参与 Orleans 交易的有声张的面孔。
    /// </summary>
    /// <typeparam name="TState">交易状态类型。</typeparam>
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
        /// 获取当前交易的识别符号。
        /// </summary>
        public string CurrentTransactionId => TransactionContext.GetRequiredTransactionInfo().Id;

        private bool detectReentrancy;

        /// <summary>
        /// 初始化 <see cref="TransactionalState{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="transactionalStateConfiguration">交易状态配置。</param>
        /// <param name="contextAccessor">当前 grain 上下文的存取器。</param>
        /// <param name="copier">用于分离交易状态版本的复印机。</param>
        /// <param name="grainRuntime">运行时间 grain 。</param>
        /// <param name="logger">日志器。</param>
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
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> 无效。</exception>
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

            // 可在锁定下读取访问
            return this.queue.RWLock.EnterLock<TResult>(info.TransactionId, info.Priority, info.Timeout, recordedaccesses, true, info.UseExclusiveLock,
                () =>
                {
                    // 检查我们的记录是否因为我们等待时过期而丢失
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<TState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到交易时间戳
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    LogDebugUpdateLockRead(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 记录此信息在交易信息数据结构中读取
                    info.RecordRead(this.participantId, record.Timestamp);

                    // 执行读作
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
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> 无效。</exception>
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

            // 强制的快照在环境交易锁外 所以它会打破连环性
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
                    // 检查等待时是否过期
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<TState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到交易时间戳
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    // 与最新状态的链接
                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    // 如果这是首篇写作, 请填写一份深度的状态副本
                    if (!record.HasCopiedState)
                    {
                        record.State = this.copier.DeepCopy(record.State);
                        record.SequenceNumber++;
                        record.HasCopiedState = true;
                    }

                    LogDebugUpdateLockWrite(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 记录此交易信息数据结构中的写入
                    info.RecordWrite(this.participantId, record.Timestamp);

                    // 执行写作
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

        // 创建队列前设置可能失败 。
        private Task OnStop(CancellationToken ct) => this.queue?.StopAsync(ct) ?? Task.CompletedTask;

        private static void SetupResourceFactory(IGrainContext context, string stateName, TransactionQueue<TState> queue)
        {
            // 将资源工厂添加到 grain 上下文
            context.RegisterResourceFactory<ITransactionalResource>(stateName, () => new TransactionalResource<TState>(queue));

            // grain 上下文添加 tm 工厂
            context.RegisterResourceFactory<ITransactionManager>(stateName, () => new TransactionManager<TState>(queue));
        }

        private async Task OnSetupState(Action<IGrainContext, string, TransactionQueue<TState>> setupResourceFactory, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            this.participantId = new ParticipantId(this.config.StateName, this.context.GrainReference, this.config.SupportedRoles);

            var storageFactory = this.context.ActivationServices.GetRequiredService<INamedTransactionalStateStorageFactory>();
            ITransactionalStateStorage<TState> storage = storageFactory.Create<TState>(this.config.StorageName, this.config.StateName);

            // 设置交易处理管道
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

            // 恢复状态
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
